import argparse
import json
from dataclasses import asdict

from myaws_win import load_config
from myaws_win.service import MyAwsService
from myaws_win.tray_app import TrayApp


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="MyAWS for Windows tray")
    parser.add_argument("--config", help="Path to config.json", default=None)
    parser.add_argument("--update-pricing", action="store_true", help="Update AWS instance pricing and exit")
    parser.add_argument("--snapshot", action="store_true", help="Print current snapshot JSON and exit")
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    config = load_config(args.config)
    service = MyAwsService(config)

    if args.update_pricing:
        service.update_pricing()
        print("Pricing updated")
        return

    if args.snapshot:
        snapshot = service.get_snapshot()
        print(json.dumps(asdict(snapshot), default=str, indent=2))
        return

    app = TrayApp(config)
    app.run()


if __name__ == "__main__":
    main()
