import json
import os
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Dict, List, Tuple


VmTypeList = List[Tuple[str, List[Tuple[str, str]]]]


DEFAULT_VM_TYPES: VmTypeList = [
    (
        "m5",
        [
            (".4xlarge", "(  16 vcpu, 64Gb vram )"),
            (".12xlarge", "(  48 vcpu, 192Gb vram )"),
            (".24xlarge", "(  96 vcpu, 384Gb vram )"),
        ],
    ),
    (
        "m6i",
        [
            (".4xlarge", "(  16 vcpu, 64Gb vram )"),
            (".12xlarge", "(  48 vcpu, 192Gb vram )"),
            (".24xlarge", "(  96 vcpu, 384Gb vram )"),
            (".32xlarge", "( 128 vcpu, 512Gb vram )"),
        ],
    ),
    (
        "m7i",
        [
            (".4xlarge", "(  16 vcpu, 64Gb vram )"),
            (".12xlarge", "(  48 vcpu, 192Gb vram )"),
            (".24xlarge", "(  96 vcpu, 384Gb vram )"),
            (".48xlarge", "( 192 vcpu, 768Gb vram )"),
        ],
    ),
    (
        "c5",
        [
            (".4xlarge", "(  16 vcpu, 32Gb vram )"),
            (".9xlarge", "(  36 vcpu, 72Gb vram )"),
            (".18xlarge", "(  72 vcpu, 144Gb vram )"),
            (".24xlarge", "(  96 vcpu, 192Gb vram )"),
        ],
    ),
    (
        "c6i",
        [
            (".4xlarge", "(  16 vcpu, 32Gb vram )"),
            (".12xlarge", "(  48 vcpu, 96Gb vram )"),
            (".24xlarge", "(  96 vcpu, 192Gb vram )"),
            (".32xlarge", "( 128 vcpu, 256Gb vram )"),
        ],
    ),
    (
        "c7i",
        [
            (".4xlarge", "(  16 vcpu, 32Gb vram )"),
            (".12xlarge", "(  48 vcpu, 96Gb vram )"),
            (".24xlarge", "(  96 vcpu, 192Gb vram )"),
            (".48xlarge", "( 192 vcpu, 384Gb vram )"),
        ],
    ),
    ("u-6tb1", [(".112xlarge", "( 448 vcpu, 6Tb vram )")]),
]


@dataclass
class AppConfig:
    aws_owner_id: str = "615416975922"
    aws_key_name: str = "pvdabeel@mac.com"
    aws_security_group_id: str = "sg-bce547d1"
    aws_region: str = "eu-central-1"
    aws_location_name: str = "EU (Frankfurt)"
    aws_operating_system: str = "Linux"
    aws_profile: str = ""
    aws_executable: str = "aws"
    ssh_executable: str = "ssh"
    ssh_user: str = "root"
    ssh_known_hosts_file: str = ""
    state_dir: str = ""
    preferred_currency: str = "EUR"
    default_vmtype_update: str = "m7i.12xlarge"
    default_vmtype_rebuild: str = "m7i.48xlarge"
    update_command: str = "update"
    rebuild_command: str = "fullupdate"
    refresh_interval_seconds: int = 900
    vm_types: VmTypeList = field(default_factory=lambda: DEFAULT_VM_TYPES.copy())

    def resolve_state_dir(self) -> Path:
        if self.state_dir:
            path = Path(self.state_dir).expanduser()
        else:
            appdata = os.getenv("APPDATA")
            if appdata:
                path = Path(appdata) / "MyAWS"
            else:
                path = Path.home() / ".state" / "myaws"
        path.mkdir(parents=True, exist_ok=True)
        return path

    def resolve_known_hosts(self) -> str:
        if self.ssh_known_hosts_file:
            return str(Path(self.ssh_known_hosts_file).expanduser())
        return str(Path.home() / ".ssh" / "amazon-vms")


def _deep_update(base: Dict[str, Any], override: Dict[str, Any]) -> Dict[str, Any]:
    output = dict(base)
    for key, value in override.items():
        if isinstance(value, dict) and isinstance(output.get(key), dict):
            output[key] = _deep_update(output[key], value)
        else:
            output[key] = value
    return output


def load_config(config_path: str | None = None) -> AppConfig:
    defaults = AppConfig()
    default_path = defaults.resolve_state_dir() / "config.json"
    if config_path:
        source = Path(config_path)
    else:
        source = default_path

    if not source.exists():
        source.parent.mkdir(parents=True, exist_ok=True)
        source.write_text(json.dumps(asdict(defaults), indent=2), encoding="utf-8")
        return defaults

    raw = json.loads(source.read_text(encoding="utf-8"))
    merged = _deep_update(asdict(defaults), raw)
    return AppConfig(**merged)
