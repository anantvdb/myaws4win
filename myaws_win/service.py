import datetime
import json
import threading
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, List, Tuple

import awspricing
import six
from currency_converter import CurrencyConverter
from tinydb import Query, TinyDB

from .aws_cli import AwsCli
from .config import AppConfig


@dataclass
class InstanceView:
    instance_id: str
    image_id: str
    state: str
    instance_type: str
    public_dns: str
    public_ip: str
    launch_time: str


@dataclass
class ImageView:
    image_id: str
    name: str
    snapshot_id: str | None


@dataclass
class AppSnapshot:
    timestamp: str
    images: List[ImageView] = field(default_factory=list)
    instances_by_image: Dict[str, List[InstanceView]] = field(default_factory=dict)
    volumes_count: int = 0
    volumes_gb: int = 0
    snapshots_count: int = 0
    snapshots_gb: int = 0
    monthly_cost_total: float = 0.0
    monthly_cost_items: List[Tuple[str, float, str]] = field(default_factory=list)
    daily_cost_items: List[Tuple[str, float]] = field(default_factory=list)


class MyAwsService:
    def __init__(self, config: AppConfig):
        self.config = config
        self.cli = AwsCli(config)
        self.state_dir = config.resolve_state_dir()
        self.database = TinyDB(self.state_dir / "myawspricing.json")
        self.converter = CurrencyConverter()
        self.lock = threading.Lock()

    def _clear_tinydb(self) -> None:
        if hasattr(self.database, "drop_tables"):
            self.database.drop_tables()
            return
        if hasattr(self.database, "purge_tables"):
            self.database.purge_tables()
            return
        if hasattr(self.database, "purge"):
            self.database.purge()
            return
        raise RuntimeError("Cannot clear TinyDB")

    def update_pricing(self) -> None:
        with self.lock:
            self._clear_tinydb()
            ec2_offer = awspricing.offer("AmazonEC2")
            for vm_group, vm_type_list in self.config.vm_types:
                for vm_type, _ in vm_type_list:
                    value = "n/a"
                    try:
                        sku = ec2_offer.search_skus(
                            instance_type=vm_group + vm_type,
                            operating_system=self.config.aws_operating_system,
                            tenancy="Shared",
                            location=self.config.aws_location_name,
                            licenseModel="No License required",
                            preInstalledSw="NA",
                            capacitystatus="Used",
                        ).pop()
                        value = next(
                            six.itervalues(
                                next(
                                    six.itervalues(ec2_offer._offer_data[sku]["terms"]["OnDemand"])
                                )["priceDimensions"]
                            )
                        )["pricePerUnit"]["USD"]
                    except Exception:
                        value = "n/a"
                    self.database.insert({"type": vm_group + vm_type, "pricing": value})
            self.database.insert({"timestamp": datetime.datetime.now().strftime("%Y-%m-%d %H:%M")})

    def _cache_file(self, prefix: str, today: datetime.date) -> Path:
        return self.state_dir / f"{prefix}-{today.strftime('%Y%m%d')}.json"

    def _get_cost_payload(self, granularity: str) -> Dict[str, Any]:
        today = datetime.date.today()
        month_start = today.replace(day=1)
        if today == month_start:
            month_start = (month_start - datetime.timedelta(days=1)).replace(day=1)
        prefix = "myaws-costs-monthly" if granularity == "MONTHLY" else "myaws-costs-daily"
        file_path = self._cache_file(prefix, today)
        if file_path.exists():
            return json.loads(file_path.read_text(encoding="utf-8"))
        try:
            payload = self.cli.run_json(
                [
                    "ce",
                    "get-cost-and-usage",
                    "--time-period",
                    f"Start={month_start.strftime('%Y-%m-%d')},End={today.strftime('%Y-%m-%d')}",
                    "--granularity",
                    granularity,
                    "--metrics",
                    "BlendedCost",
                    "--group-by",
                    "Type=DIMENSION,Key=SERVICE",
                ]
            )
        except RuntimeError as exc:
            message = str(exc)
            if "DataUnavailableException" in message or "GetCostAndUsage" in message:
                return {"ResultsByTime": []}
            raise
        file_path.write_text(json.dumps(payload), encoding="utf-8")
        return payload

    def _all_instances(self) -> List[InstanceView]:
        raw = self.cli.run_json(
            [
                "ec2",
                "describe-instances",
                "--query",
                "Reservations[*].Instances[*].{PublicDnsName:PublicDnsName,State:State,InstanceType:InstanceType,PublicIpAddress:PublicIpAddress,InstanceId:InstanceId,ImageId:ImageId,LaunchTime:LaunchTime}",
            ]
        )
        values: List[InstanceView] = []
        for reservation in raw:
            for instance in reservation:
                values.append(
                    InstanceView(
                        instance_id=instance.get("InstanceId", ""),
                        image_id=instance.get("ImageId", ""),
                        state=instance.get("State", {}).get("Name", "unknown"),
                        instance_type=instance.get("InstanceType", ""),
                        public_dns=instance.get("PublicDnsName", ""),
                        public_ip=instance.get("PublicIpAddress", ""),
                        launch_time=instance.get("LaunchTime", ""),
                    )
                )
        return values

    def get_snapshot(self) -> AppSnapshot:
        images_raw = self.cli.run_json(
            [
                "ec2",
                "describe-images",
                "--owners",
                self.config.aws_owner_id,
                "--query",
                "Images[*].{ImageId:ImageId,Name:Name,SnapshotId:BlockDeviceMappings[0].Ebs.SnapshotId}",
            ]
        )
        volumes = self.cli.run_json(["ec2", "describe-volumes", "--query", "Volumes[*].{Size:Size}"])
        snapshots = self.cli.run_json(
            [
                "ec2",
                "describe-snapshots",
                "--owner-ids",
                self.config.aws_owner_id,
                "--query",
                "Snapshots[*].{Size:VolumeSize}",
            ]
        )
        monthly = self._get_cost_payload("MONTHLY")
        daily = self._get_cost_payload("DAILY")
        instances = self._all_instances()

        image_views = [
            ImageView(
                image_id=image.get("ImageId", ""),
                name=image.get("Name", "unnamed"),
                snapshot_id=image.get("SnapshotId"),
            )
            for image in images_raw
        ]
        instance_map: Dict[str, List[InstanceView]] = {}
        for item in instances:
            instance_map.setdefault(item.image_id, []).append(item)

        monthly_items: List[Tuple[str, float, str]] = []
        monthly_total = 0.0
        for month_period in monthly.get("ResultsByTime", []):
            for group in month_period.get("Groups", []):
                amount = float(group["Metrics"]["BlendedCost"]["Amount"])
                monthly_total += amount
                monthly_items.append((group["Keys"][0], amount, group["Metrics"]["BlendedCost"]["Unit"]))

        daily_items: List[Tuple[str, float]] = []
        for day in daily.get("ResultsByTime", []):
            day_total = 0.0
            for group in day.get("Groups", []):
                day_total += float(group["Metrics"]["BlendedCost"]["Amount"])
            daily_items.append((day["TimePeriod"]["Start"], day_total))

        return AppSnapshot(
            timestamp=datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
            images=image_views,
            instances_by_image=instance_map,
            volumes_count=len(volumes),
            volumes_gb=sum(int(v["Size"]) for v in volumes),
            snapshots_count=len(snapshots),
            snapshots_gb=sum(int(s["Size"]) for s in snapshots),
            monthly_cost_total=monthly_total,
            monthly_cost_items=monthly_items,
            daily_cost_items=daily_items,
        )

    def instance_price(self, instance_type: str) -> str:
        query = Query()
        rows = self.database.search(query.type == instance_type)
        if not rows or rows[0]["pricing"] == "n/a":
            return "n/a"
        usd_price = float(rows[0]["pricing"])
        if self.config.preferred_currency == "USD":
            value = usd_price
            symbol = "$"
        else:
            value = self.converter.convert(usd_price, "USD", self.config.preferred_currency)
            symbol = "€" if self.config.preferred_currency == "EUR" else "$"
        return f"{symbol}{value:.4f}/h"

    def run_instance(self, image_id: str, instance_type: str) -> str:
        result = self.cli.run_json(
            [
                "ec2",
                "run-instances",
                "--image-id",
                image_id,
                "--instance-type",
                instance_type,
                "--ebs-optimized",
                "--key-name",
                self.config.aws_key_name,
                "--security-group-ids",
                self.config.aws_security_group_id,
            ]
        )
        return result["Instances"][0]["InstanceId"]

    def start_instance(self, instance_id: str) -> None:
        self.cli.run_no_output(["ec2", "start-instances", "--instance-ids", instance_id])

    def stop_instance(self, instance_id: str) -> None:
        self.cli.run_no_output(["ec2", "stop-instances", "--instance-ids", instance_id, "--force"])

    def terminate_instance(self, instance_id: str) -> None:
        self.cli.run_no_output(["ec2", "terminate-instances", "--instance-ids", instance_id])

    def terminate_instances(self, instance_ids: List[str]) -> None:
        if not instance_ids:
            return
        self.cli.run_no_output(["ec2", "terminate-instances", "--instance-ids", *instance_ids])

    def create_image(self, instance_id: str) -> str:
        name = "Linux-" + time.strftime("%Y%m%d-%Hh%M")
        response = self.cli.run_json(["ec2", "create-image", "--instance-id", instance_id, "--name", name])
        return response.get("ImageId", "")

    def destroy_image(self, image_id: str, snapshot_id: str, dry_run: bool = False) -> None:
        args_1 = ["ec2", "deregister-image", "--image-id", image_id]
        args_2 = ["ec2", "delete-snapshot", "--snapshot-id", snapshot_id]
        if dry_run:
            args_1.append("--dry-run")
            args_2.append("--dry-run")
        self.cli.run_no_output(args_1)
        self.cli.run_no_output(args_2)

    def open_ssh(self, dns_name: str) -> None:
        self.cli.open_ssh_terminal(dns_name)

    def write_serial_console_log(self, instance_id: str) -> Path:
        path = self.state_dir / f"myaws-{instance_id}.console.log"
        output = self.cli.run_text(["ec2", "get-console-output", "--output", "text", "--instance-id", instance_id])
        path.write_text(output, encoding="utf-8", errors="replace")
        return path

    def screenshot_base64(self, instance_id: str) -> str:
        data = self.cli.run_json(["ec2", "get-console-screenshot", "--instance-id", instance_id])
        return data.get("ImageData", "")

    def update_image(self, ami_to_update: str, ami_snapshot_id: str, rebuild: bool = False) -> str:
        vm_type = (
            self.config.default_vmtype_rebuild if rebuild else self.config.default_vmtype_update
        )
        remote_cmd = self.config.rebuild_command if rebuild else self.config.update_command
        instance_id = self.run_instance(ami_to_update, vm_type)
        try:
            self.cli.run_no_output(["ec2", "wait", "instance-running", "--instance-ids", instance_id])
            data = self.cli.run_json(
                [
                    "ec2",
                    "describe-instances",
                    "--instance-ids",
                    instance_id,
                    "--query",
                    "Reservations[*].Instances[*].{PublicDnsName:PublicDnsName}",
                ]
            )
            dns_name = data[0][0]["PublicDnsName"]
            time.sleep(60)
            outcome = self.cli.run_ssh(dns_name, remote_cmd)
            if outcome != 0:
                raise RuntimeError("Remote update command failed")
            new_image = self.create_image(instance_id)
            self.cli.run_no_output(["ec2", "wait", "image-available", "--image-ids", new_image])
            self.terminate_instance(instance_id)
            self.destroy_image(ami_to_update, ami_snapshot_id)
            return new_image
        except Exception:
            self.terminate_instance(instance_id)
            raise
