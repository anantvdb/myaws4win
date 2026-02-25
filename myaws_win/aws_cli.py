import json
import shutil
import subprocess
from pathlib import Path
from typing import Any, List

from .config import AppConfig


class AwsCli:
    def __init__(self, config: AppConfig):
        self.config = config
        self.aws_executable = self._resolve_aws_executable(config.aws_executable)
        self.ssh_executable = self._resolve_ssh_executable(config.ssh_executable)

    def _resolve_aws_executable(self, configured: str) -> str:
        candidates = [configured, "aws", "aws.exe", "aws.cmd"]
        return self._resolve_executable(candidates, "AWS CLI")

    def _resolve_ssh_executable(self, configured: str) -> str:
        candidates = [configured, "ssh", "ssh.exe"]
        return self._resolve_executable(candidates, "OpenSSH client")

    @staticmethod
    def _resolve_executable(candidates: List[str], display_name: str) -> str:
        for candidate in candidates:
            if not candidate:
                continue
            resolved = shutil.which(candidate)
            if resolved:
                return resolved
            candidate_path = Path(candidate).expanduser()
            if candidate_path.exists():
                return str(candidate_path)
        primary = next((value for value in candidates if value), "")
        raise RuntimeError(
            f"{display_name} executable not found. Checked: {', '.join([c for c in candidates if c])}. "
            f"Install {display_name} and ensure it is in PATH, or set the full path in config (current: '{primary}')."
        )

    def _base(self) -> List[str]:
        base = [self.aws_executable]
        if self.config.aws_profile:
            base += ["--profile", self.config.aws_profile]
        if self.config.aws_region:
            base += ["--region", self.config.aws_region]
        return base

    def _run(self, args: List[str]) -> subprocess.CompletedProcess[str]:
        command = self._base() + args
        try:
            return subprocess.run(
                command,
                capture_output=True,
                text=True,
                check=True,
                encoding="utf-8",
                errors="replace",
            )
        except subprocess.CalledProcessError as exc:
            stderr = (exc.stderr or "").strip()
            stdout = (exc.stdout or "").strip()
            detail = stderr or stdout or str(exc)
            raise RuntimeError(f"AWS CLI call failed: {detail}") from exc

    def run_json(self, args: List[str]) -> Any:
        output = self.run_text(args + ["--output", "json"])
        if not output:
            return {}
        return json.loads(output)

    def run_text(self, args: List[str]) -> str:
        completed = self._run(args)
        return completed.stdout.strip()

    def run_no_output(self, args: List[str]) -> None:
        self._run(args)

    def run_ssh(self, host: str, remote_command: str) -> int:
        ssh_args = [
            self.ssh_executable,
            "-q",
            "-t",
            "-o",
            "StrictHostKeyChecking=no",
            "-o",
            f"UserKnownHostsFile={self.config.resolve_known_hosts()}",
            f"{self.config.ssh_user}@{host}",
            f"bash -icl '{remote_command}'",
        ]
        return subprocess.call(ssh_args)

    def open_ssh_terminal(self, host: str) -> None:
        known_hosts = self.config.resolve_known_hosts()
        cmd = (
            f'"{self.ssh_executable}" -q -o StrictHostKeyChecking=no '
            f'-o UserKnownHostsFile="{known_hosts}" {self.config.ssh_user}@{host}'
        )
        subprocess.Popen(["cmd", "/c", "start", "cmd", "/k", cmd], cwd=str(Path.home()))
