import datetime
import subprocess
import threading
from pathlib import Path
from typing import Callable, Optional

import pystray
from PIL import Image, ImageDraw

from .config import AppConfig
from .service import AppSnapshot, ImageView, InstanceView, MyAwsService


class TrayApp:
    def __init__(self, config: AppConfig):
        self.config = config
        self.service = MyAwsService(config)
        self.state: Optional[AppSnapshot] = None
        self.last_error = ""
        self._stop_event = threading.Event()
        self._refresh_lock = threading.Lock()
        self.icon = pystray.Icon("MyAWS", self._build_icon(), "MyAWS")
        self.icon.menu = pystray.Menu(self._dynamic_menu)

    def _build_icon(self) -> Image.Image:
        size = 64
        image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        draw = ImageDraw.Draw(image)
        draw.rounded_rectangle((4, 4, 60, 60), radius=12, fill=(33, 150, 243, 255))
        draw.text((16, 18), "AWS", fill=(255, 255, 255, 255))
        return image

    def run(self) -> None:
        self.refresh(force=True)
        worker = threading.Thread(target=self._auto_refresh_loop, daemon=True)
        worker.start()
        self.icon.run()

    def _auto_refresh_loop(self) -> None:
        while not self._stop_event.is_set():
            self._stop_event.wait(self.config.refresh_interval_seconds)
            if self._stop_event.is_set():
                break
            self.refresh(force=False)

    def _notify(self, title: str, message: str) -> None:
        try:
            self.icon.notify(message, title)
        except Exception:
            pass

    def _run_async(self, action: Callable[[], object], action_name: str) -> None:
        def runner() -> None:
            try:
                action()
                self.last_error = ""
                self._notify("MyAWS", f"{action_name} completed")
            except Exception as exc:
                self.last_error = str(exc)
                self._notify("MyAWS", f"{action_name} failed: {exc}")
            finally:
                self.refresh(force=True)

        threading.Thread(target=runner, daemon=True).start()

    def _async_menu_item(
        self,
        label: str,
        action: Callable[[], object],
        action_name: str,
        *,
        enabled: bool = True,
    ) -> pystray.MenuItem:
        return pystray.MenuItem(
            label,
            lambda *_: self._run_async(action, action_name),
            enabled=enabled,
        )

    def refresh(self, force: bool) -> None:
        if not self._refresh_lock.acquire(blocking=False):
            return
        try:
            self.state = self.service.get_snapshot()
            self.last_error = ""
            if force:
                self._notify("MyAWS", "Data refreshed")
        except Exception as exc:
            self.last_error = str(exc)
        finally:
            self._refresh_lock.release()
            self.icon.update_menu()

    def _dynamic_menu(self) -> pystray.Menu:
        return pystray.Menu(
            pystray.MenuItem(lambda _: self._title(), None, enabled=False),
            self._async_menu_item("Refresh now", lambda: self.refresh(True), "Refresh"),
            self._async_menu_item("Update AWS pricing", self.service.update_pricing, "Pricing update"),
            pystray.MenuItem("Images", pystray.Menu(*self._images_menu())),
            pystray.MenuItem("Storage", pystray.Menu(*self._storage_menu())),
            pystray.MenuItem("Costs", pystray.Menu(*self._costs_menu())),
            pystray.MenuItem("Open state folder", lambda *_: self._open_state_folder()),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem("Quit", lambda *_: self._quit()),
        )

    def _title(self) -> str:
        if self.last_error:
            return f"MyAWS | ERROR: {self.last_error}"
        if not self.state:
            return "MyAWS | Loading..."
        return f"MyAWS | {len(self.state.images)} images | refreshed {self.state.timestamp}"

    def _images_menu(self) -> list[pystray.MenuItem]:
        if not self.state:
            return [pystray.MenuItem("No data", None, enabled=False)]
        if not self.state.images:
            return [
                pystray.MenuItem("No AMIs found", None, enabled=False),
                pystray.MenuItem("Available VM options", pystray.Menu(*self._vm_options_menu())),
            ]
        return [
            pystray.MenuItem(image.name, pystray.Menu(*self._single_image_menu(image)))
            for image in sorted(self.state.images, key=lambda x: x.name.lower())
        ]

    def _vm_options_menu(self) -> list[pystray.MenuItem]:
        items: list[pystray.MenuItem] = []
        for vm_group, vm_list in self.config.vm_types:
            for vm_suffix, vm_desc in vm_list:
                vm_type = vm_group + vm_suffix
                price = self.service.instance_price(vm_type)
                label = f"{vm_type} {vm_desc} {price}"
                items.append(pystray.MenuItem(label, None, enabled=False))
            items.append(pystray.Menu.SEPARATOR)
        if items and items[-1] == pystray.Menu.SEPARATOR:
            items.pop()
        return items

    def _single_image_menu(self, image: ImageView) -> list[pystray.MenuItem]:
        image_instances = self.state.instances_by_image.get(image.image_id, []) if self.state else []
        instance_ids = [i.instance_id for i in image_instances if i.state in {"running", "stopped", "pending"}]
        return [
            pystray.MenuItem("Deploy new VM", pystray.Menu(*self._deploy_menu_items(image))),
            pystray.MenuItem("Instances", pystray.Menu(*self._instances_menu(image_instances))),
            self._async_menu_item(
                "Terminate all VMs",
                lambda: self.service.terminate_instances(instance_ids),
                f"Terminate all VMs for {image.name}",
                enabled=bool(instance_ids),
            ),
            pystray.Menu.SEPARATOR,
            self._async_menu_item(
                "Image: Update",
                lambda: self.service.update_image(image.image_id, image.snapshot_id or "", rebuild=False),
                f"Update image {image.name}",
                enabled=bool(image.snapshot_id),
            ),
            self._async_menu_item(
                "Image: Rebuild",
                lambda: self.service.update_image(image.image_id, image.snapshot_id or "", rebuild=True),
                f"Rebuild image {image.name}",
                enabled=bool(image.snapshot_id),
            ),
            self._async_menu_item(
                "Image: Destroy",
                lambda: self.service.destroy_image(image.image_id, image.snapshot_id or "", dry_run=False),
                f"Destroy image {image.name}",
                enabled=bool(image.snapshot_id),
            ),
        ]

    def _deploy_menu_items(self, image: ImageView) -> list[pystray.MenuItem]:
        items: list[pystray.MenuItem] = []
        for vm_group, vm_list in self.config.vm_types:
            for vm_suffix, vm_desc in vm_list:
                vm_type = vm_group + vm_suffix
                price = self.service.instance_price(vm_type)
                label = f"{vm_type} {vm_desc} {price}"
                items.append(
                    self._async_menu_item(
                        label,
                        lambda _img=image.image_id, _t=vm_type: self.service.run_instance(_img, _t),
                        f"Deploy {vm_type}",
                    )
                )
            items.append(pystray.Menu.SEPARATOR)
        if items and items[-1] == pystray.Menu.SEPARATOR:
            items.pop()
        return items

    def _instances_menu(self, instances: list[InstanceView]) -> list[pystray.MenuItem]:
        if not instances:
            return [pystray.MenuItem("No instances", None, enabled=False)]
        items: list[pystray.MenuItem] = []
        for instance in instances:
            uptime_label = self._uptime_label(instance)
            title = f"{instance.instance_id} | {instance.state} | {instance.instance_type} | {instance.public_ip or '-'} | {uptime_label}"
            items.append(pystray.MenuItem(title, pystray.Menu(*self._instance_actions(instance))))
        return items

    def _instance_actions(self, instance: InstanceView) -> list[pystray.MenuItem]:
        return [
            self._async_menu_item(
                "Connect SSH",
                lambda: self.service.open_ssh(instance.public_dns),
                f"Connect {instance.instance_id}",
                enabled=instance.state == "running" and bool(instance.public_dns),
            ),
            self._async_menu_item(
                "Start",
                lambda: self.service.start_instance(instance.instance_id),
                f"Start {instance.instance_id}",
                enabled=instance.state == "stopped",
            ),
            self._async_menu_item(
                "Stop",
                lambda: self.service.stop_instance(instance.instance_id),
                f"Stop {instance.instance_id}",
                enabled=instance.state == "running",
            ),
            self._async_menu_item(
                "Terminate",
                lambda: self.service.terminate_instance(instance.instance_id),
                f"Terminate {instance.instance_id}",
                enabled=instance.state in {"running", "stopped", "pending", "stopping"},
            ),
            self._async_menu_item(
                "Create image",
                lambda: self.service.create_image(instance.instance_id),
                f"Create image from {instance.instance_id}",
                enabled=instance.state == "stopped",
            ),
            self._async_menu_item(
                "Save serial console log",
                lambda: self.service.write_serial_console_log(instance.instance_id),
                f"Console log {instance.instance_id}",
                enabled=instance.state != "terminated",
            ),
            self._async_menu_item(
                "Get screenshot",
                lambda: self.service.screenshot_base64(instance.instance_id),
                f"Screenshot {instance.instance_id}",
                enabled=instance.state == "running",
            ),
        ]

    def _uptime_label(self, instance: InstanceView) -> str:
        if not instance.launch_time:
            return "n/a"
        try:
            launch = instance.launch_time[:19]
            dt = datetime.datetime.strptime(launch, "%Y-%m-%dT%H:%M:%S")
            delta = datetime.datetime.utcnow() - dt
            days = int(delta.total_seconds() // 86400)
            hours = int((delta.total_seconds() % 86400) // 3600)
            minutes = int((delta.total_seconds() % 3600) // 60)
            return f"{days:02d}d:{hours:02d}h:{minutes:02d}m"
        except Exception:
            return "n/a"

    def _storage_menu(self) -> list[pystray.MenuItem]:
        if not self.state:
            return [pystray.MenuItem("No data", None, enabled=False)]
        return [
            pystray.MenuItem(
                f"Volumes: {self.state.volumes_count} objects, {self.state.volumes_gb} GiB",
                None,
                enabled=False,
            ),
            pystray.MenuItem(
                f"Snapshots: {self.state.snapshots_count} objects, {self.state.snapshots_gb} GiB",
                None,
                enabled=False,
            ),
        ]

    def _costs_menu(self) -> list[pystray.MenuItem]:
        if not self.state:
            return [pystray.MenuItem("No data", None, enabled=False)]
        items: list[pystray.MenuItem] = [
            pystray.MenuItem(f"Month total: {self.state.monthly_cost_total:.2f} USD", None, enabled=False),
            pystray.MenuItem("Monthly breakdown", pystray.Menu(*self._monthly_items())),
            pystray.MenuItem("Daily totals", pystray.Menu(*self._daily_items())),
        ]
        return items

    def _monthly_items(self) -> list[pystray.MenuItem]:
        if not self.state or not self.state.monthly_cost_items:
            return [pystray.MenuItem("No data", None, enabled=False)]
        return [
            pystray.MenuItem(f"{name}: {amount:.4f} {unit}", None, enabled=False)
            for name, amount, unit in self.state.monthly_cost_items
        ]

    def _daily_items(self) -> list[pystray.MenuItem]:
        if not self.state or not self.state.daily_cost_items:
            return [pystray.MenuItem("No data", None, enabled=False)]
        return [
            pystray.MenuItem(f"{date}: {amount:.4f} USD", None, enabled=False)
            for date, amount in self.state.daily_cost_items
        ]

    def _open_state_folder(self) -> None:
        folder = Path(self.service.state_dir)
        self._run_async(lambda: subprocess.Popen(["explorer", str(folder)]), "Open state folder")

    def _quit(self) -> None:
        self._stop_event.set()
        self.icon.stop()
