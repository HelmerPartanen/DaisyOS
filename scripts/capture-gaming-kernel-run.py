#!/usr/bin/env python3
"""Capture one comparable boot of the stock or optional Zen gaming kernel."""
import argparse
import json
import platform
import re
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def output(command, timeout=10):
    try:
        result = subprocess.run(command, text=True, capture_output=True, timeout=timeout)
        return result.stdout.strip() if result.returncode == 0 else None
    except (OSError, subprocess.TimeoutExpired):
        return None


def installed(package):
    return output(["pacman", "-Q", package])


def kernel_config(release):
    candidates = [Path(f"/boot/config-{release}"), Path("/proc/config.gz")]
    for path in candidates:
        try:
            if path.name == "config.gz":
                result = subprocess.run(["zcat", str(path)], text=True, capture_output=True, timeout=5)
                if result.returncode == 0:
                    return result.stdout
            else:
                return path.read_text()
        except (OSError, subprocess.TimeoutExpired):
            pass
    return ""


def config_values(config):
    keys = ("CONFIG_HZ", "CONFIG_PREEMPT", "CONFIG_PREEMPT_DYNAMIC", "CONFIG_PREEMPT_VOLUNTARY",
            "CONFIG_PREEMPT_NONE", "CONFIG_SCHED_BORE", "CONFIG_SCHED_CLASS_EXT", "CONFIG_X86_64_VERSION")
    values = {}
    for key in keys:
        match = re.search(rf"^{re.escape(key)}=(.*)$", config, re.MULTILINE)
        disabled = re.search(rf"^# {re.escape(key)} is not set$", config, re.MULTILINE)
        values[key] = match.group(1) if match else ("n" if disabled else None)
    return values


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("profile", choices=("stock", "zen"), help="The kernel booted for this run")
    parser.add_argument("--output", type=Path, required=True, help="New JSON report path")
    parser.add_argument("--duration", type=int, default=300, help="Desktop resource capture length in seconds")
    args = parser.parse_args()
    if args.duration < 300:
        parser.error("Use at least 300 seconds to capture a settled idle interval")
    if not shutil.which("pacman"):
        parser.error("This capture requires an Arch-based installed system")
    expected = "linux" if args.profile == "stock" else "linux-zen"
    if not installed(expected):
        parser.error(f"Expected package {expected!r} is not installed")
    if args.output.exists():
        parser.error(f"Refusing to overwrite {args.output}")
    release = platform.release()
    kernel_owner = output(["pacman", "-Qqo", f"/usr/lib/modules/{release}"])
    if kernel_owner != expected:
        parser.error(f"Profile {args.profile!r} expects {expected}, but booted kernel {release!r} is owned by {kernel_owner!r}")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    stem = args.output.with_suffix("")
    metadata_path = stem.with_name(stem.name + ".metadata.json")
    if metadata_path.exists():
        parser.error(f"Refusing to overwrite {metadata_path}")

    metadata = {
        "kernelProfile": args.profile,
        "kernelRelease": release,
        "kernelPackage": installed(expected),
        "bootedKernelModuleDirectoryOwner": kernel_owner,
        "stockKernelPackage": installed("linux"),
        "zenKernelPackage": installed("linux-zen"),
        "kernelConfig": config_values(kernel_config(release)),
        "nvidiaModuleVersionForBootedKernel": output(["modinfo", "-k", release, "nvidia", "-F", "version"]),
        "nvidiaModuleLoaded": Path("/sys/module/nvidia").exists(),
        "dkmsStatusForBootedKernel": output(["dkms", "status", "-k", release]),
        "capturedAtUtc": datetime.now(timezone.utc).isoformat(),
        "host": platform.node(),
        "cpu": platform.processor(),
        "machine": platform.machine(),
        "kernelCommandLine": Path("/proc/cmdline").read_text().strip(),
    }
    metadata_path.write_text(json.dumps(metadata, indent=2) + "\n")

    session_report = stem.with_name(stem.name + ".session.json")
    response_report = stem.with_name(stem.name + ".responsiveness.json")
    commands = [
        [sys.executable, str(ROOT / "scripts/benchmark-desktop-session.py"), "--output", str(session_report),
         "--duration", str(args.duration), "--state", "diagnostic", "--metadata", str(metadata_path)],
        [sys.executable, str(ROOT / "scripts/benchmark-os-performance.py"), "run", "--build",
         "--configuration", "Release", "--samples", "7", "--warmups", "2", "--skip-chrome",
         "--output", str(response_report)],
    ]
    for command in commands:
        result = subprocess.run(command, cwd=ROOT)
        if result.returncode:
            print(f"Capture stopped with exit code {result.returncode}: {' '.join(command)}", file=sys.stderr)
            return result.returncode
    args.output.write_text(json.dumps({
        "protocol": "daisyos-gaming-kernel-capture-v1",
        "profile": args.profile,
        "metadata": str(metadata_path),
        "desktopSession": str(session_report),
        "responsiveness": str(response_report),
    }, indent=2) + "\n")
    print(f"Kernel run captured: {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
