
# MyAWS for Windows (Python tray app)

This repository now includes a Windows-native tray implementation that preserves the original MyAWS features without xbar/BitBar.

The legacy macOS script is still present in `myaws.15m.py`.

## Features

- View AMIs and related EC2 instances from tray menus
- Deploy/start/stop/terminate EC2 virtual machines
- Create, update, rebuild, and destroy AMIs
- Open SSH sessions in a dedicated Windows terminal
- View storage usage (volumes and snapshots)
- View monthly and daily AWS Cost Explorer totals
- Refresh data manually or on a background interval

## Project layout

- `main.py`: app entrypoint
- `myaws_win/config.py`: configuration schema and loader
- `myaws_win/aws_cli.py`: AWS/SSH command runner
- `myaws_win/service.py`: business logic and AWS data/actions
- `myaws_win/tray_app.py`: Windows tray UI with `pystray`
- `requirements.txt`: Python dependencies
- `myaws-windows.spec`: PyInstaller packaging spec

## Prerequisites (Windows)

1. Python 3.11+
2. AWS CLI v2 (`aws --version`)
3. OpenSSH client (`ssh -V`)
4. AWS credentials configured (`aws configure`)

## Installation

```powershell
python -m venv .venv
.\.venv\Scripts\activate
pip install -r requirements.txt
```

## First run

```powershell
python main.py --update-pricing
python main.py
```

On first run, a default config file is generated at:

- `%APPDATA%\MyAWS\config.json`

Edit this file to set values such as:

- `aws_owner_id`
- `aws_key_name`
- `aws_security_group_id`
- `aws_region`
- `aws_profile`

## Optional commands

```powershell
python main.py --update-pricing
python main.py --snapshot
python main.py --config C:\path\to\config.json
```

## Packaging as EXE

```powershell
pip install pyinstaller
pyinstaller myaws-windows.spec
```

The packaged app will be generated in `dist\myaws-windows.exe`.

## Validation checklist

- Confirm tray icon appears and menu refresh works
- Validate deploy/start/stop/terminate for a test instance
- Validate image create/update/rebuild/destroy on non-critical AMIs
- Validate cost and storage values are shown
- Validate SSH terminal opens and connects
- Validate log export to the state directory

## Licence

GPL v3
