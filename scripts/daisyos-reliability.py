#!/usr/bin/env python3
"""DaisyOS installed-system reliability capture and acceptance protocol."""

import argparse
import csv
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import re
import shutil
import signal
import socket
import subprocess
import sys
import threading
import time


ROOT = Path(__file__).resolve().parents[1]
PROTOCOL = "daisyos-reliability-v1"
MIB = 1024 * 1024
DEFAULT_MANIFEST = ROOT / "tests" / "reliability" / "everyday-v1.json"
MANDATORY_RAW = ("environment.json", "journal-kernel.txt", "journal-boot.txt", "coredumps.json")

EVENT_RULES = (
    ("kernel-oom", "measured", re.compile(r"out of memory:|oom-kill:|killed process \d+", re.I)),
    ("systemd-oomd", "measured", re.compile(r"systemd-oomd.*(?:killed|kill(?:ing)?).*(?:cgroup|slice|scope|memory pressure)", re.I)),
    ("memory-policy-warning", "measured", re.compile(r"systemd-oomd.*(?:no swap|degraded|failed|ineligible)", re.I)),
    ("gpu-driver", "measured", re.compile(r"gpu (?:reset|hang)|xid \d+|amdgpu.*(?:reset|timeout)|i915.*(?:hang|reset)", re.I)),
    ("storage-io", "measured", re.compile(r"i/o error|buffer i/o|blk_update_request|ext4-fs error|btrfs.*error|nvme.*(?:reset|timeout)", re.I)),
    ("thermal-power", "measured", re.compile(r"thermal.*(?:critical|shutdown|throttl)|mce:.*(?:hardware error|machine check)|power.*(?:failure|shutdown)", re.I)),
    ("suspend-resume", "measured", re.compile(r"suspending system|system resumed|suspend entry|suspend exit", re.I)),
    ("application-fault", "measured", re.compile(r"segfault|sigsegv|signal 11|status=11/segv", re.I)),
    ("application-abort", "measured", re.compile(r"sigabrt|signal 6|status=6/abrt|free\(\): invalid pointer", re.I)),
    ("explicit-signal", "inferred", re.compile(r"sigterm|signal 15|status=15/term|sigint|signal 2", re.I)),
    ("supervisor-action", "measured", re.compile(r"daisyos-session.*(?:retry|stopped unexpectedly|fallback)|start request repeated too quickly", re.I)),
)


def utc_now():
    return datetime.now(timezone.utc).isoformat()


def atomic_json(path, value):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n")
    temporary.replace(path)


def read_text(path, default=""):
    try:
        return Path(path).read_text(errors="replace")
    except OSError:
        return default


def parse_kib_map(text):
    values = {}
    for line in text.splitlines():
        match = re.match(r"^([^:]+):\s+(\d+)\s+kB$", line)
        if match:
            values[match.group(1)] = int(match.group(2)) * 1024
    return values


def command_result(argv, timeout=20, env=None):
    started = time.monotonic()
    try:
        result = subprocess.run(argv, capture_output=True, text=True, timeout=timeout, env=env)
        return {"argv": argv, "exitCode": result.returncode, "stdout": result.stdout,
                "stderr": result.stderr, "durationSeconds": time.monotonic() - started}
    except FileNotFoundError:
        return {"argv": argv, "exitCode": None, "error": "command-not-found", "durationSeconds": time.monotonic() - started}
    except subprocess.TimeoutExpired as error:
        return {"argv": argv, "exitCode": None, "error": "timeout", "stdout": error.stdout or "",
                "stderr": error.stderr or "", "durationSeconds": time.monotonic() - started}
    except OSError as error:
        return {"argv": argv, "exitCode": None, "error": type(error).__name__, "durationSeconds": time.monotonic() - started}


def write_command(raw, name, argv, timeout=20):
    result = command_result(argv, timeout)
    (raw / name).write_text(result.get("stdout", "") + result.get("stderr", ""))
    return {key: value for key, value in result.items() if key not in ("stdout", "stderr")}


def collection_failed(command, path):
    if not command or command.get("exitCode") == 0:
        return False
    if path.name == "coredumps.json" and "No coredumps found" in read_text(path):
        return False
    return True


def redact(text):
    home = str(Path.home())
    text = text.replace(home, "$HOME") if home != "/" else text
    text = re.sub(r"\b(?:[A-Za-z0-9._%+-]+)@(?:[A-Za-z0-9.-]+\.[A-Za-z]{2,})\b", "<email>", text)
    text = re.sub(r"(?i)(token|password|secret|authorization)=?\S+", r"\1=<redacted>", text)
    return text


def environment_snapshot():
    mem = parse_kib_map(read_text("/proc/meminfo"))
    gpu = []
    for path in sorted(Path("/sys/class/drm").glob("card*/device/uevent")):
        gpu.append({"path": str(path), "uevent": read_text(path)})
    return {
        "capturedAt": utc_now(), "host": socket.gethostname(), "kernel": platform.release(),
        "platform": platform.platform(), "machine": platform.machine(), "bootId": read_text("/proc/sys/kernel/random/boot_id").strip(),
        "memTotalBytes": mem.get("MemTotal"), "usableRamClassGiB": round(mem.get("MemTotal", 0) / (1024 ** 3), 2),
        "desktop": os.environ.get("XDG_CURRENT_DESKTOP"), "sessionType": os.environ.get("XDG_SESSION_TYPE"),
        "renderer": command_result(["glxinfo", "-B"], 10),
        "gpu": gpu, "cmdlineSha256": hashlib.sha256(read_text("/proc/cmdline").encode()).hexdigest(),
    }


def capture_raw(output):
    raw = output / "raw"
    raw.mkdir(parents=True, exist_ok=False)
    atomic_json(raw / "environment.json", environment_snapshot())
    commands = {
        "journal-kernel.txt": ["journalctl", "-b", "-k", "--no-pager", "--output=short-iso-precise"],
        "journal-boot.txt": ["journalctl", "-b", "--no-pager", "--output=short-iso-precise"],
        "journal-user.txt": ["journalctl", "--user", "-b", "--no-pager", "--output=short-iso-precise"],
        "coredumps.json": ["coredumpctl", "--json=short", "list"],
        "system-status.txt": ["systemctl", "--failed", "--no-pager"],
        "user-status.txt": ["systemctl", "--user", "--failed", "--no-pager"],
        "oomd-status.txt": ["oomctl", "--no-pager"],
        "swap.txt": ["swapon", "--show=NAME,TYPE,SIZE,USED,PRIO", "--bytes"],
        "zram.txt": ["zramctl", "--output-all"],
        "block-devices.json": ["lsblk", "--json", "--output", "NAME,TYPE,SIZE,FSTYPE,MOUNTPOINTS,MODEL,SERIAL,ROTA"],
        "storage-health-scan.txt": ["smartctl", "--scan-open"],
        "firmware.txt": ["dmidecode", "--type", "bios", "--type", "system"],
        "thermal.txt": ["sensors", "-j"],
        "packages.txt": ["pacman", "-Q"],
        "app-slice.txt": ["systemctl", "--user", "show", "daisyos-apps.slice"],
        "core-slice.txt": ["systemctl", "show", "daisyos-core.slice"],
    }
    metadata = {name: write_command(raw, name, argv, 60) for name, argv in commands.items()}
    for device in sorted(list(Path("/dev").glob("nvme*n1")) + list(Path("/dev").glob("sd?"))):
        name = "storage-health-" + device.name + ".txt"
        metadata[name] = write_command(raw, name, ["smartctl", "--all", str(device)], 30)
    for source, target in (("/proc/meminfo", "proc-meminfo.txt"), ("/proc/swaps", "proc-swaps.txt"),
                           ("/proc/pressure/memory", "memory-pressure.txt"), ("/proc/vmstat", "vmstat.txt")):
        (raw / target).write_text(read_text(source))
    atomic_json(raw / "collection.json", {"protocol": PROTOCOL, "commands": metadata})
    missing = [name for name in MANDATORY_RAW if not (raw / name).exists()]
    return raw, missing


def classify_text(text):
    events = []
    seen = set()
    for line_number, line in enumerate(text.splitlines(), 1):
        for category, basis, pattern in EVENT_RULES:
            if pattern.search(line):
                fingerprint = (category, line.strip())
                if fingerprint not in seen:
                    seen.add(fingerprint)
                    events.append({"category": category, "basis": basis, "line": line_number,
                                   "evidence": redact(line.strip())[:2000]})
                break
    return events


def classify_coredumps(text):
    events = []
    records = []
    for line_number, line in enumerate(text.splitlines(), 1):
        try:
            record = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(record, list):
            records.extend((line_number, item) for item in record if isinstance(item, dict))
        elif isinstance(record, dict):
            records.append((line_number, record))
    for line_number, record in records:
        signal_value = str(record.get("SIGNAL", record.get("COREDUMP_SIGNAL", ""))).upper()
        category = {"6": "application-abort", "ABRT": "application-abort", "SIGABRT": "application-abort",
                    "11": "application-fault", "SEGV": "application-fault", "SIGSEGV": "application-fault"}.get(signal_value)
        if not category and signal_value in {"9", "KILL", "SIGKILL"}:
            category = "unexplained-signal"
        if category:
            events.append({"category": category, "basis": "measured", "line": line_number,
                           "evidence": {"pid": record.get("COREDUMP_PID", record.get("PID")),
                                        "executable": record.get("COREDUMP_EXE", record.get("EXE")),
                                        "signal": signal_value,
                                        "timestamp": record.get("COREDUMP_TIMESTAMP", record.get("TIMESTAMP"))}})
    return events


def process_samples(limit=10):
    rows = []
    for proc in Path("/proc").iterdir():
        if not proc.name.isdigit():
            continue
        values = parse_kib_map(read_text(proc / "smaps_rollup"))
        if not values:
            continue
        command = read_text(proc / "comm").strip() or "unknown"
        stat_fields = read_text(proc / "stat").split()
        start_ticks = int(stat_fields[21]) if len(stat_fields) > 21 and stat_fields[21].isdigit() else None
        rows.append({"pid": int(proc.name), "command": command, "pssBytes": values.get("Pss", 0),
                     "startTicks": start_ticks,
                     "privateBytes": values.get("Private_Clean", 0) + values.get("Private_Dirty", 0),
                     "rssBytes": values.get("Rss", 0), "swapPssBytes": values.get("SwapPss", 0)})
    return sorted(rows, key=lambda row: row["pssBytes"], reverse=True)[:limit]


def parse_psi(text):
    result = {}
    for line in text.splitlines():
        fields = line.split()
        if not fields:
            continue
        result[fields[0]] = {key: float(value) if key != "total" else int(value)
                             for key, value in (field.split("=", 1) for field in fields[1:])}
    return result


def sample_system():
    mem = parse_kib_map(read_text("/proc/meminfo"))
    vmstat = {parts[0]: int(parts[1]) for line in read_text("/proc/vmstat").splitlines()
              if len(parts := line.split()) == 2 and parts[1].isdigit()}
    kernel = {
        "slabBytes": mem.get("Slab", 0), "pageTablesBytes": mem.get("PageTables", 0),
        "pageCacheBytes": mem.get("Cached", 0) + mem.get("SReclaimable", 0),
        "swapUsedBytes": max(0, mem.get("SwapTotal", 0) - mem.get("SwapFree", 0)),
    }
    gpu = {}
    nvidia = command_result(["nvidia-smi", "--query-compute-apps=pid,used_memory", "--format=csv,noheader,nounits"], 3)
    if nvidia.get("exitCode") == 0:
        gpu["nvidiaComputeMiB"] = nvidia.get("stdout", "").splitlines()
    nvidia_total = command_result(["nvidia-smi", "--query-gpu=index,memory.used", "--format=csv,noheader,nounits"], 3)
    if nvidia_total.get("exitCode") == 0:
        gpu["nvidiaDeviceUsedMiB"] = nvidia_total.get("stdout", "").splitlines()
    for path in Path("/sys/class/drm").glob("card*/device/mem_info_*_used"):
        value = read_text(path).strip()
        if value.isdigit():
            gpu[path.name] = int(value)
    thermal = {}
    for path in Path("/sys/class/thermal").glob("thermal_zone*/temp"):
        value = read_text(path).strip()
        if re.fullmatch(r"-?\d+", value):
            thermal[path.parent.name] = int(value)
    throttle = {}
    for path in Path("/sys/devices/system/cpu").glob("cpu*/thermal_throttle/*_throttle_count"):
        value = read_text(path).strip()
        if value.isdigit():
            throttle[str(path)] = int(value)
    cgroups = {}
    uid = int(os.environ.get("SUDO_UID", os.getuid()))
    candidates = {
        "apps": Path(f"/sys/fs/cgroup/user.slice/user-{uid}.slice/user@{uid}.service/daisyos-apps.slice"),
        "core": Path("/sys/fs/cgroup/daisyos-core.slice"),
    }
    for name, root in candidates.items():
        if root.exists():
            cgroups[name] = {"memoryCurrentBytes": int(read_text(root / "memory.current", "0").strip() or 0),
                             "memoryEvents": read_text(root / "memory.events")}
    return {"timestamp": utc_now(), "monotonicSeconds": time.monotonic(), "memTotalBytes": mem.get("MemTotal", 0),
            "memAvailableBytes": mem.get("MemAvailable", 0), "usedDefinitionBytes": mem.get("MemTotal", 0) - mem.get("MemAvailable", 0),
            "kernel": kernel, "psiMemory": parse_psi(read_text("/proc/pressure/memory")),
            "vmstat": {key: vmstat.get(key, 0) for key in ("pswpin", "pswpout", "pgscan_kswapd", "pgsteal_kswapd", "oom_kill")},
            "loadAverage": list(os.getloadavg()), "topProcesses": process_samples(), "gpu": gpu,
            "thermalMilliCelsius": thermal, "thermalThrottleCounts": throttle, "cgroups": cgroups,
            "diskstats": read_text("/proc/diskstats")}


def validate_manifest(manifest):
    if manifest.get("protocol") != PROTOCOL or manifest.get("version") != 1:
        raise ValueError("Manifest must use daisyos-reliability-v1 version 1")
    profiles = manifest.get("profiles")
    if not isinstance(profiles, list) or not profiles:
        raise ValueError("Manifest needs at least one profile")
    for profile in profiles:
        if not profile.get("id") or not isinstance(profile.get("steps"), list):
            raise ValueError("Every profile needs an id and steps")
        for step in profile["steps"]:
            if step.get("type") not in ("command", "checkpoint", "sleep"):
                raise ValueError(f"Unsupported replay step: {step.get('type')}")
            if step["type"] == "command" and (not isinstance(step.get("argv"), list) or not step["argv"]):
                raise ValueError("Command steps require a non-empty argv array")


def manifest_hash(manifest):
    return hashlib.sha256(json.dumps(manifest, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def profiled_command(argv, profiler, run_dir, cycle, name):
    safe_name = re.sub("[^a-z0-9-]", "-", name.lower())
    profile_root = run_dir / "profiles"
    if profiler == "none":
        return argv, None
    profile_root.mkdir(exist_ok=True)
    if profiler == "heaptrack":
        return ["heaptrack", "--output", str(profile_root / f"{cycle:05d}-{safe_name}.gz"), *argv], None
    if profiler == "valgrind":
        return ["valgrind", "--leak-check=full", "--show-leak-kinds=all",
                f"--log-file={profile_root / f'{cycle:05d}-{safe_name}.log'}", *argv], None
    if profiler == "dotnet-counters":
        return ["dotnet-counters", "collect", "--output", str(profile_root / f"{cycle:05d}-{safe_name}.nettrace"), "--", *argv], None
    if profiler == "asan-lsan":
        env = os.environ.copy()
        env.update({"ASAN_OPTIONS": "detect_leaks=1:abort_on_error=1", "LSAN_OPTIONS": "exitcode=23"})
        return argv, env
    raise ValueError(f"Unsupported profiler: {profiler}")


def execute_step(step, run_dir, cycle, profiler="none", maximum_seconds=None, checkpoint_results=None):
    result = {"startedAt": utc_now(), "cycle": cycle, "type": step["type"], "name": step.get("name")}
    if step["type"] == "sleep":
        seconds = max(0, float(step.get("seconds", 0)))
        if maximum_seconds is not None:
            seconds = min(seconds, max(0, maximum_seconds))
        time.sleep(seconds)
        result["durationSeconds"] = seconds
    elif step["type"] == "checkpoint":
        check = (checkpoint_results or {}).get(step.get("name"))
        result["status"] = "human-pass" if check and check.get("passed") is True else ("human-fail" if check else "pending-human-check")
        result["instructions"] = step.get("instructions", "Verify visible and interactive behavior.")
        if check:
            result["humanEvidence"] = check.get("evidence")
    else:
        argv = [part.replace("${FIXTURES}", str(run_dir / "fixtures")) for part in step["argv"]]
        argv, profile_env = profiled_command(argv, profiler, run_dir, cycle, step.get("name", "command"))
        timeout = max(1, int(step.get("timeoutSeconds", 60)))
        if maximum_seconds is not None:
            timeout = max(1, min(timeout, math.ceil(maximum_seconds)))
        command = command_result(argv, timeout, profile_env)
        result.update({key: value for key, value in command.items() if key not in ("stdout", "stderr")})
        log = run_dir / "workload" / f"cycle-{cycle:05d}-{re.sub('[^a-z0-9-]', '-', step.get('name', 'command').lower())}.log"
        log.write_text(redact(command.get("stdout", "") + command.get("stderr", "")))
        result["log"] = str(log.relative_to(run_dir))
        result["status"] = "passed" if command.get("exitCode") == 0 else "failed"
    result["finishedAt"] = utc_now()
    return result


def create_fixtures(root):
    root.mkdir(parents=True, exist_ok=False)
    files = root / "files"
    files.mkdir()
    for index in range(100):
        (files / f"document-{index:03d}.txt").write_text((f"DaisyOS reliability fixture {index}\n" * 50))
    (root / "document.txt").write_text("DaisyOS deterministic document fixture.\n" * 200)
    (root / "media.txt").write_text("Local media fixture placeholder; manifest commands must not require network access.\n")


def new_run_directory(base, label):
    timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    target = base / f"{timestamp}-{label}"
    target.mkdir(parents=True, exist_ok=False)
    return target


def summarize_run(run_dir, strict=True):
    raw = run_dir / "raw"
    try:
        run_metadata = json.loads(read_text(run_dir / "run.json", "{}"))
    except json.JSONDecodeError:
        run_metadata = {}
    if not isinstance(run_metadata, dict):
        run_metadata = {}
    samples = []
    sample_path = run_dir / "samples.jsonl"
    if sample_path.exists():
        for line in sample_path.read_text().splitlines():
            try:
                samples.append(json.loads(line))
            except json.JSONDecodeError:
                pass
    journal_names = ("journal-kernel.txt", "journal-boot.txt", "journal-user.txt")
    event_root = run_dir / "end" if run_metadata.get("kind") == "run" else raw
    journal = "\n".join(read_text(event_root / name) for name in journal_names)
    events = classify_text(journal) + classify_coredumps(read_text(event_root / "coredumps.json"))
    missing = [name for name in MANDATORY_RAW if not (raw / name).exists()]
    try:
        collection = json.loads(read_text(raw / "collection.json", "{}"))
    except json.JSONDecodeError:
        collection = {}
    for name in MANDATORY_RAW:
        command = collection.get("commands", {}).get(name)
        if collection_failed(command, raw / name):
            missing.append(f"{name} (collection failed)")
    if run_metadata.get("kind") == "run":
        end_root = run_dir / "end"
        try:
            end_collection = json.loads(read_text(end_root / "collection.json", "{}"))
        except json.JSONDecodeError:
            end_collection = {}
        for name in ("journal-kernel.txt", "journal-boot.txt", "coredumps.json"):
            if not (end_root / name).exists():
                missing.append(f"end/{name}")
            command = end_collection.get("commands", {}).get(name)
            if collection_failed(command, end_root / name):
                missing.append(f"end/{name} (collection failed)")
    failures = []
    if not samples:
        failures.append("No resource samples were captured")
    categories = {event["category"] for event in events}
    if categories & {"kernel-oom", "systemd-oomd"}:
        failures.append("OOM kill observed")
    unexplained = [event for event in events if event["category"] in {"application-fault", "application-abort", "unexplained-signal"}]
    if unexplained:
        failures.append("Application crash lacks an attributed root cause")
    if categories & {"gpu-driver", "storage-io", "thermal-power"}:
        failures.append("Hardware or driver reliability event observed")
    if missing:
        failures.append("Mandatory evidence missing: " + ", ".join(missing))
    workload = []
    workload_path = run_dir / "workload-results.jsonl"
    if workload_path.exists():
        workload = [json.loads(line) for line in workload_path.read_text().splitlines() if line.strip()]
        if any(item.get("status") in {"failed", "human-fail"} for item in workload):
            failures.append("Workload replay step failed")
        if any(item.get("status") == "pending-human-check" for item in workload):
            failures.append("Human checkpoints remain pending")
    minimum_available = min((sample.get("memAvailableBytes", math.inf) for sample in samples), default=None)
    maximum_used = max((sample.get("usedDefinitionBytes", 0) for sample in samples), default=None)
    maximum_full = max((sample.get("psiMemory", {}).get("full", {}).get("avg10", 0) for sample in samples), default=None)
    if minimum_available is not None and minimum_available < 1024 * MIB:
        failures.append("MemAvailable fell below 1 GiB")
    if maximum_full is not None and maximum_full > 0:
        failures.append("Memory PSI full avg10 was nonzero")
    leak_candidates = detect_growth(samples)
    if leak_candidates:
        failures.append("Process PSS did not return within 10% of its warmed baseline")
    top = samples[-1]["topProcesses"] if samples else []
    environment = json.loads(read_text(raw / "environment.json", "{}"))
    if not isinstance(environment, dict):
        environment = {}
    if run_metadata.get("kind") == "capture" and maximum_used is not None and maximum_used > 1536 * MIB:
        failures.append("Settled idle MemTotal - MemAvailable exceeded 1.5 GiB")
    gpu_identity = hashlib.sha256(json.dumps(environment.get("gpu", []), sort_keys=True).encode()).hexdigest()
    report = {"protocol": PROTOCOL, "generatedAt": utc_now(), "run": str(run_dir),
              "status": "failed" if failures else "passed", "failures": failures, "missingEvidence": missing,
              "eventCounts": {name: sum(event["category"] == name for event in events) for name in sorted(categories)},
              "events": events, "sampleCount": len(samples), "minimumMemAvailableBytes": minimum_available,
              "maximumUsedDefinitionBytes": maximum_used, "maximumPsiFullAvg10": maximum_full,
              "topProcessesByPss": top, "kernelAtFinalSample": samples[-1]["kernel"] if samples else None,
              "growthCandidates": leak_candidates,
              "workloadSteps": len(workload), "humanCheckpointsPending": sum(item.get("status") == "pending-human-check" for item in workload),
              "comparisonIdentity": {"host": environment.get("host"), "machine": environment.get("machine"),
                                     "memTotalBytes": environment.get("memTotalBytes"), "gpuHash": gpu_identity,
                                     "manifestHash": run_metadata.get("manifestHash")},
              "measurementNote": "Process PSS, kernel pools, and GPU allocations are separate accounting domains and are not summed."}
    atomic_json(run_dir / "summary.json", report)
    write_markdown(run_dir / "findings.md", report)
    if strict and failures:
        return report, 1
    return report, 0


def mib(value):
    return "unavailable" if value is None else f"{value / MIB:.1f} MiB"


def detect_growth(samples):
    if len(samples) < 5:
        return []
    series = {}
    for index, sample in enumerate(samples):
        for row in sample.get("topProcesses", []):
            key = (row.get("pid"), row.get("startTicks"), row.get("command"))
            series.setdefault(key, []).append((index, row.get("pssBytes", 0)))
    candidates = []
    for (pid, start_ticks, command), values in series.items():
        if len(values) < max(4, len(samples) // 2):
            continue
        warmed = values[max(0, len(values) // 5 - 1)][1]
        final = values[-1][1]
        increases = sum(right[1] > left[1] for left, right in zip(values, values[1:]))
        if warmed >= 10 * MIB and final > warmed * 1.10 and increases >= (len(values) - 1) * .6:
            candidates.append({"pid": pid, "startTicks": start_ticks, "command": command, "warmedPssBytes": warmed,
                               "finalPssBytes": final, "growthPercent": (final / warmed - 1) * 100})
    return sorted(candidates, key=lambda item: item["growthPercent"], reverse=True)


def write_markdown(path, report):
    lines = ["# DaisyOS Everyday Reliability Findings", "", f"Status: **{report['status'].upper()}**", "",
             "## Evidence summary", "", f"- Samples: {report['sampleCount']}",
             f"- Minimum MemAvailable: {mib(report['minimumMemAvailableBytes'])}",
             f"- Peak `MemTotal - MemAvailable`: {mib(report['maximumUsedDefinitionBytes'])}",
             f"- Peak memory PSI full avg10: {report['maximumPsiFullAvg10'] if report['maximumPsiFullAvg10'] is not None else 'unavailable'}",
             f"- Pending human checkpoints: {report['humanCheckpointsPending']}", ""]
    lines += ["## Failures", ""] + ([f"- {item}" for item in report["failures"]] or ["- None observed."]) + [""]
    lines += ["## Classified events", ""] + ([f"- {key}: {value}" for key, value in report["eventCounts"].items()] or ["- None observed."]) + [""]
    lines += ["## Growth candidates", ""] + ([f"- {item['command']} (PID {item['pid']}): {item['growthPercent']:.1f}%" for item in report["growthCandidates"]] or ["- None detected by the warmed-plateau heuristic."]) + [""]
    lines += ["## Top memory consumers", "", "| Process | PID | PSS | Private | RSS |", "|---|---:|---:|---:|---:|"]
    for row in report["topProcessesByPss"]:
        lines.append(f"| {row['command']} | {row['pid']} | {mib(row['pssBytes'])} | {mib(row['privateBytes'])} | {mib(row['rssBytes'])} |")
    lines += ["", "Kernel pools at final sample are recorded separately in `summary.json`. GPU allocations are reported separately when the driver exposes them.", "",
              "## Conclusions and remaining risks", "", "This report records observations only. A root cause is not inferred solely from temporal proximity. Missing hardware, profiler, or 24-hour evidence remains unverified.", ""]
    path.write_text("\n".join(lines))


def compare_runs(before_path, after_path, output):
    before = json.loads(Path(before_path).read_text())
    after = json.loads(Path(after_path).read_text())
    if before.get("protocol") != PROTOCOL or after.get("protocol") != PROTOCOL:
        raise ValueError("Both summaries must use the reliability v1 protocol")
    if before.get("comparisonIdentity") != after.get("comparisonIdentity"):
        raise ValueError("Hardware or workload identity differs; matched runs are required")
    def delta(field):
        left, right = before.get(field), after.get(field)
        return None if left is None or right is None else right - left
    result = {"protocol": PROTOCOL, "generatedAt": utc_now(), "before": str(before_path), "after": str(after_path),
              "statusChange": [before.get("status"), after.get("status")],
              "deltas": {field: delta(field) for field in ("minimumMemAvailableBytes", "maximumUsedDefinitionBytes", "maximumPsiFullAvg10")},
              "eventCountChanges": {key: after.get("eventCounts", {}).get(key, 0) - before.get("eventCounts", {}).get(key, 0)
                                    for key in sorted(set(before.get("eventCounts", {})) | set(after.get("eventCounts", {})))},
              "qualified": before.get("sampleCount", 0) > 0 and after.get("sampleCount", 0) > 0}
    atomic_json(output, result)
    return result


def command_capture(args):
    run_dir = new_run_directory(Path(args.output).resolve(), args.label)
    atomic_json(run_dir / "run.json", {"protocol": PROTOCOL, "kind": "capture", "startedAt": utc_now()})
    capture_raw(run_dir)
    sample = sample_system()
    (run_dir / "samples.jsonl").write_text(json.dumps(sample, sort_keys=True) + "\n")
    report, status = summarize_run(run_dir, args.strict)
    print(run_dir)
    return status


def command_run(args):
    if args.duration <= 0 or args.interval <= 0:
        raise ValueError("Duration and sample interval must be positive")
    manifest = json.loads(Path(args.manifest).read_text())
    validate_manifest(manifest)
    checkpoint_results = {}
    if args.checkpoint_results:
        checkpoint_results = json.loads(Path(args.checkpoint_results).read_text())
        if not isinstance(checkpoint_results, dict):
            raise ValueError("Checkpoint results must be a JSON object keyed by full checkpoint name")
    run_dir = new_run_directory(Path(args.output).resolve(), args.label)
    create_fixtures(run_dir / "fixtures")
    (run_dir / "workload").mkdir()
    atomic_json(run_dir / "manifest.json", manifest)
    capture_raw(run_dir)
    metadata = {"protocol": PROTOCOL, "manifestHash": manifest_hash(manifest), "startedAt": utc_now(),
                "startedEpoch": time.time(), "kind": "run", "durationSeconds": args.duration,
                "sampleIntervalSeconds": args.interval, "profiler": args.profiler}
    atomic_json(run_dir / "run.json", metadata)
    deadline = time.monotonic() + args.duration
    cycle = 0
    stop_sampling = threading.Event()
    def sampling_worker():
        with (run_dir / "samples.jsonl").open("a") as samples:
            while True:
                samples.write(json.dumps(sample_system(), sort_keys=True) + "\n"); samples.flush()
                remaining = deadline - time.monotonic()
                if remaining <= 0 or stop_sampling.wait(min(args.interval, remaining)):
                    break
    sampler = threading.Thread(target=sampling_worker, name="daisyos-reliability-sampler", daemon=True)
    sampler.start()
    with (run_dir / "workload-results.jsonl").open("a") as results:
        while time.monotonic() < deadline:
            cycle += 1
            for profile in manifest["profiles"]:
                for step in profile["steps"]:
                    step = step | {"name": f"{profile['id']}-{step.get('name', step['type'])}"}
                    outcome = execute_step(step, run_dir, cycle, args.profiler, deadline - time.monotonic(), checkpoint_results)
                    results.write(json.dumps(outcome, sort_keys=True) + "\n"); results.flush()
                    if time.monotonic() >= deadline:
                        break
                if time.monotonic() >= deadline:
                    break
    stop_sampling.set()
    sampler.join(timeout=max(5, args.interval + 5))
    # Capture the end-of-run boot evidence separately; original files remain immutable.
    end = run_dir / "end"
    end.mkdir()
    since = "@" + str(metadata["startedEpoch"])
    end_commands = {
        "journal-kernel.txt": ["journalctl", "-b", "-k", "--since", since, "--no-pager", "--output=short-iso-precise"],
        "journal-boot.txt": ["journalctl", "-b", "--since", since, "--no-pager", "--output=short-iso-precise"],
        "journal-user.txt": ["journalctl", "--user", "-b", "--since", since, "--no-pager", "--output=short-iso-precise"],
        "coredumps.json": ["coredumpctl", "--since", since, "--json=short", "list"],
    }
    end_metadata = {name: write_command(end, name, argv, 60) for name, argv in end_commands.items()}
    atomic_json(end / "collection.json", {"protocol": PROTOCOL, "commands": end_metadata})
    report, status = summarize_run(run_dir, True)
    print(run_dir)
    return status


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    sub = result.add_subparsers(dest="command", required=True)
    capture = sub.add_parser("capture", help="Capture a point-in-time evidence bundle")
    capture.add_argument("--output", default="artifacts/reliability")
    capture.add_argument("--label", default="capture")
    capture.add_argument("--strict", action="store_true")
    capture.set_defaults(function=command_capture)
    run = sub.add_parser("run", help="Run a replay workload while sampling")
    run.add_argument("--manifest", default=str(DEFAULT_MANIFEST))
    run.add_argument("--duration", type=int, default=8 * 60 * 60)
    run.add_argument("--interval", type=float, default=30)
    run.add_argument("--output", default="artifacts/reliability")
    run.add_argument("--label", default="everyday")
    run.add_argument("--profiler", choices=("none", "dotnet-counters", "heaptrack", "valgrind", "asan-lsan"), default="none")
    run.add_argument("--checkpoint-results", help="JSON evidence for required human checkpoints")
    run.set_defaults(function=command_run)
    summary = sub.add_parser("summarize", help="Rebuild findings from an existing run")
    summary.add_argument("run_directory")
    summary.add_argument("--no-strict", action="store_true")
    summary.set_defaults(function=lambda args: summarize_run(Path(args.run_directory), not args.no_strict)[1])
    compare = sub.add_parser("compare", help="Compare two summary.json files")
    compare.add_argument("before")
    compare.add_argument("after")
    compare.add_argument("--output", required=True)
    compare.set_defaults(function=lambda args: (compare_runs(args.before, args.after, Path(args.output)), 0)[1])
    return result


def main():
    os.umask(0o077)
    args = parser().parse_args()
    try:
        return args.function(args)
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"reliability harness: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
