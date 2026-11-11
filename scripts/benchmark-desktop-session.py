#!/usr/bin/env python3
"""Capture a repeatable DaisyOS desktop-session resource timeline.

The sampler is read-only. It records attributable process PSS (summed only for
the selected desktop processes), CPU deltas, cgroup memory where available,
kernel pressure/swap state, and display context. It never stops processes.
"""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import platform
import re
import statistics
import time


MIB = 1024 * 1024
PROCESS_ROLES = (
    ("shell", re.compile(r"^DaisyOS\.Shell$", re.I)),
    ("compositor", re.compile(r"^kwin_(?:wayland|x11)$", re.I)),
    ("daisyos-service", re.compile(r"^(?:DaisyOS\.Continuity|daisy-launch|daisyos-librepods|daisyos-polkit-agent|daisyos-desktop-services)$", re.I)),
    ("desktop-platform", re.compile(r"^(?:pipewire|pipewire-pulse|wireplumber|xdg-desktop-portal(?:-.*)?|xdg-document-portal|xdg-permission-store|dbus-broker|kded6)$", re.I)),
)


def read_text(path, default=""):
    try:
        return Path(path).read_text(errors="replace")
    except OSError:
        return default


def parse_kib_fields(path):
    result = {}
    for line in read_text(path).splitlines():
        match = re.match(r"^(\w+):\s+(\d+)\s+kB$", line)
        if match:
            result[match.group(1)] = int(match.group(2)) * 1024
    return result


def classify_process(comm, arguments):
    executable = Path(arguments.split(" ", 1)[0]).name if arguments else comm
    if "--desktop-services" in arguments:
        return "daisyos-service"
    if executable.lower() == "daisyos.continuity":
        return "daisyos-service"
    if executable.lower() == "daisyos.shell" and ("--shell-session" in arguments or "--real-services" in arguments):
        return "shell"
    for role, pattern in PROCESS_ROLES:
        if role == "shell":
            continue
        if pattern.match(comm) or pattern.match(executable):
            return role
    return None


def process_snapshot(previous=None, elapsed=None):
    processes = []
    cgroup_paths = {}
    current_cpu = {}
    denied = 0
    target_uid = int(os.environ.get("SUDO_UID", os.getuid()))
    boot_time = None
    for line in read_text("/proc/stat").splitlines():
        if line.startswith("btime "):
            try:
                boot_time = int(line.split()[1])
            except (ValueError, IndexError):
                pass
            break
    for proc in Path("/proc").iterdir():
        if not proc.name.isdigit():
            continue
        pid = int(proc.name)
        comm = read_text(proc / "comm").strip()
        if not comm:
            continue
        uid_line = next((line for line in read_text(proc / "status").splitlines() if line.startswith("Uid:")), "")
        try:
            if int(uid_line.split()[1]) != target_uid:
                continue
        except (ValueError, IndexError):
            continue
        args = read_text(proc / "cmdline").replace("\0", " ")
        role = classify_process(comm, args)
        if role is None:
            continue
        stat = read_text(proc / "stat")
        try:
            fields = stat[stat.rfind(")") + 2:].split()
            ticks = int(fields[11]) + int(fields[12])
        except (ValueError, IndexError):
            ticks = None
        if ticks is not None:
            current_cpu[str(pid)] = {"ticks": ticks, "startTicks": fields[19] if len(fields) > 19 else None}
        start_epoch = (boot_time + int(fields[19]) / os.sysconf("SC_CLK_TCK")) if boot_time is not None and ticks is not None else None
        for line in read_text(proc / "cgroup").splitlines():
            if line.startswith("0::"):
                relative = line[3:].lstrip("/")
                cgroup_paths.setdefault(relative, []).append(pid)
        values = parse_kib_fields(proc / "smaps_rollup")
        if not values:
            denied += 1
        processes.append({
            "pid": pid,
            "name": comm,
            "role": role,
            "pssBytes": values.get("Pss"),
            "rssBytes": values.get("Rss"),
            "privateBytes": values.get("Private_Clean", 0) + values.get("Private_Dirty", 0),
            "swapPssBytes": values.get("SwapPss"),
            "processStartEpochSeconds": start_epoch,
            "pssReadable": bool(values),
            "cpuPercentOfOneCore": None,
        })
    if previous and elapsed and elapsed > 0:
        hz = os.sysconf("SC_CLK_TCK")
        by_pid = {row["pid"]: row for row in processes}
        for key, value in current_cpu.items():
            before = previous.get(key)
            if before is None or before.get("startTicks") != value.get("startTicks"):
                continue
            delta = max(0, value["ticks"] - before["ticks"])
            if int(key) in by_pid:
                by_pid[int(key)]["cpuPercentOfOneCore"] = 100.0 * delta / hz / elapsed
    processes.sort(key=lambda row: row["pssBytes"] or 0, reverse=True)
    cgroups = []
    for relative, pids in sorted(cgroup_paths.items()):
        directory = Path("/sys/fs/cgroup") / relative
        current = read_text(directory / "memory.current").strip()
        cgroups.append({"path": "/" + relative, "processIds": pids,
                        "memoryCurrentBytes": int(current) if current.isdigit() else None,
                        "memoryStat": read_memory_stat(directory / "memory.stat")})
    return processes, current_cpu, denied, cgroups


def read_memory_stat(path):
    result = {}
    for line in read_text(path).splitlines():
        fields = line.split()
        if len(fields) == 2 and fields[1].isdigit():
            result[fields[0]] = int(fields[1])
    return result


def system_snapshot(previous_cpu=None, elapsed=None):
    mem = parse_kib_fields("/proc/meminfo")
    pressure = {}
    for line in read_text("/proc/pressure/memory").splitlines():
        fields = line.split()
        if fields:
            pressure[fields[0]] = {k: float(v) if k != "total" else int(v)
                                   for k, v in (field.split("=", 1) for field in fields[1:])}
    swap = []
    lines = read_text("/proc/swaps").splitlines()
    for line in lines[1:]:
        fields = line.split()
        if len(fields) >= 5:
            swap.append({"name": Path(fields[0]).name, "type": fields[1],
                         "sizeKiB": int(fields[2]), "usedKiB": int(fields[3])})
    processes, cpu, denied, cgroups = process_snapshot(previous_cpu, elapsed)
    pss = sum(row["pssBytes"] or 0 for row in processes)
    roles = {}
    for row in processes:
        bucket = roles.setdefault(row["role"], {"processCount": 0, "pssBytes": 0, "rssBytes": 0,
                                                  "privateBytes": 0, "cpuPercentOfOneCore": 0.0})
        bucket["processCount"] += 1
        bucket["pssBytes"] += row["pssBytes"] or 0
        bucket["rssBytes"] += row["rssBytes"] or 0
        bucket["privateBytes"] += row["privateBytes"] or 0
        bucket["cpuPercentOfOneCore"] += row["cpuPercentOfOneCore"] or 0
    return ({
        "timestampUtc": datetime.now(timezone.utc).isoformat(),
        "monotonicSeconds": time.monotonic(),
        "memory": {"totalBytes": mem.get("MemTotal"), "availableBytes": mem.get("MemAvailable"),
                   "usedExcludingAvailableBytes": max(0, mem.get("MemTotal", 0) - mem.get("MemAvailable", 0)),
                   "swapTotalBytes": mem.get("SwapTotal"), "swapFreeBytes": mem.get("SwapFree")},
        "swapDevices": swap,
        "memoryPressure": pressure,
        "loadAverage": list(os.getloadavg()),
        "desktopProcesses": processes,
        "byRole": roles,
        "desktopProcessesPssBytes": pss,
        "desktopProcessesPssComplete": denied == 0,
        "desktopProcessesPrivateBytes": sum(row["privateBytes"] for row in processes),
        "processesWithoutReadableSmaps": denied,
        "cgroups": cgroups,
    }, cpu)


def metadata():
    return {"host": platform.node(), "kernel": platform.release(), "machine": platform.machine(),
            "uid": int(os.environ.get("SUDO_UID", os.getuid())), "sessionId": os.environ.get("XDG_SESSION_ID"),
            "sessionType": os.environ.get("XDG_SESSION_TYPE"),
            "desktop": os.environ.get("XDG_CURRENT_DESKTOP"),
            "waylandDisplay": os.environ.get("WAYLAND_DISPLAY"),
            "display": os.environ.get("DISPLAY"),
            "protocol": "daisyos-desktop-session-v1",
            "accounting": "Sum of PSS for selected DaisyOS desktop processes; PSS apportions shared pages but may not include pages shared with processes outside the selection.",
            "processSelection": "Processes owned by the target user's UID whose executable matches a DaisyOS shell/service, KWin, audio, portal, D-Bus, or KDE session component. Other apps and other users are excluded.",
            "selection": {role: pattern.pattern for role, pattern in PROCESS_ROLES}}


def summarize(samples, session_log=None):
    def med(values):
        clean = [v for v in values if v is not None]
        return statistics.median(clean) if clean else None
    readiness_values = []
    marker = re.compile(r"\[daisy-shell-ready\]\s+pid=(\d+)\s+utc=(\S+)\s+stage=initial-desktop-rendered")
    ready_by_pid = {}
    if session_log:
        for line in read_text(session_log).splitlines():
            match = marker.search(line)
            if match:
                try:
                    ready_by_pid[int(match.group(1))] = datetime.fromisoformat(match.group(2).replace("Z", "+00:00")).timestamp()
                except ValueError:
                    continue
    shell_rows = {row["pid"]: row for sample in samples for row in sample["desktopProcesses"] if row["role"] == "shell"}
    for pid, rendered_epoch in ready_by_pid.items():
        process = shell_rows.get(pid)
        started = process.get("processStartEpochSeconds") if process else None
        if started is not None and rendered_epoch >= started:
            readiness_values.append((started, round((rendered_epoch - started) * 1000, 2)))
    ready = max(readiness_values)[1] if readiness_values else None
    return {"sampleCount": len(samples),
            "medianDesktopProcessesPssMiB": med([s["desktopProcessesPssBytes"] / MIB for s in samples]),
            "pssCompleteForEverySample": all(s["desktopProcessesPssComplete"] for s in samples),
            "medianPrivateMiB": med([s["desktopProcessesPrivateBytes"] / MIB for s in samples]),
            "medianSystemUsedExcludingAvailableMiB": med([s["memory"]["usedExcludingAvailableBytes"] / MIB for s in samples]),
            "medianDesktopCpuPercentOfOneCore": med([sum(r["cpuPercentOfOneCore"] or 0 for r in s["desktopProcesses"])
                                                        for s in samples if any(r["cpuPercentOfOneCore"] is not None for r in s["desktopProcesses"])]),
            "medianMemAvailableMiB": med([s["memory"]["availableBytes"] / MIB for s in samples if s["memory"]["availableBytes"] is not None]),
            "readinessLatencyMs": ready,
            "interactionLatenciesMs": None,
            "readinessSource": "first native desktop composition batch render-thread completion marker in the session log" if ready is not None else None,
            "interactionLatencyLimitation": "Interactive latency requires visible GUI actions; use the app startup and launch-route benchmarks."}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--duration", type=int, default=300, help="Capture duration in seconds (default: 300)")
    parser.add_argument("--interval", type=int, default=30, help="Regular sample interval (default: 30 seconds)")
    parser.add_argument("--milestones", default="0,30,300", help="Comma-separated elapsed-second milestones")
    parser.add_argument("--metadata", type=Path, help="Optional JSON workload annotations, merged into the report")
    parser.add_argument("--state", choices=("diagnostic", "fresh-login-idle"), default="diagnostic",
                        help="Use fresh-login-idle only when all controlled conditions are recorded in --metadata")
    parser.add_argument("--session-log", type=Path,
                        default=Path(os.environ.get("DAISYOS_SESSION_LOG", Path.home() / ".local/state/DaisyOS/session.log")),
                        help="Supervisor log containing the native shell readiness marker")
    args = parser.parse_args()
    if args.duration < 0 or args.interval < 1:
        parser.error("duration must be non-negative and interval at least one second")
    try:
        milestones = sorted({int(item) for item in args.milestones.split(",") if item != ""})
    except ValueError:
        parser.error("milestones must be comma-separated integer seconds")
    if any(item < 0 or item > args.duration for item in milestones):
        parser.error("milestones must fall within the capture duration")
    if args.output.exists():
        parser.error(f"refusing to overwrite existing report: {args.output}")

    annotations = json.loads(args.metadata.read_text()) if args.metadata else {}
    if not isinstance(annotations, dict):
        parser.error("--metadata must contain a JSON object")
    if args.state == "fresh-login-idle":
        required = {"buildConfiguration", "buildId", "displayConfiguration", "theme", "wallpaper",
                    "openApplicationWindows", "foregroundLoad", "backgroundFeaturesEnabled"}
        missing = sorted(required - annotations.keys())
        if missing:
            parser.error("fresh-login-idle requires --metadata fields: " + ", ".join(missing))
        if annotations["buildConfiguration"] != "Release" or annotations["openApplicationWindows"] != 0 \
                or annotations["foregroundLoad"] != "none" or annotations["backgroundFeaturesEnabled"] is not True \
                or annotations["wallpaper"] != "static":
            parser.error("fresh-login-idle metadata must describe Release, zero open app windows, no foreground load, enabled background features, and static wallpaper")

    start = time.monotonic()
    deadlines = sorted(set(milestones) | set(range(0, args.duration + 1, args.interval)))
    samples, previous_cpu, previous_time = [], None, None
    for deadline in deadlines:
        delay = start + deadline - time.monotonic()
        if delay > 0:
            time.sleep(delay)
        now = time.monotonic()
        sample, current_cpu = system_snapshot(previous_cpu, now - previous_time if previous_time else None)
        sample["elapsedSeconds"] = now - start
        samples.append(sample)
        previous_cpu, previous_time = current_cpu, now

    report = {"metadata": metadata(), "workload": {"state": args.state, "annotations": annotations},
              "milestonesSeconds": milestones, "samples": samples,
              "summary": summarize(samples, args.session_log)}
    if len(samples) and samples[-1]["elapsedSeconds"] >= 300:
        report["workload"]["fiveMinuteSettledSampleCaptured"] = True
    elif 300 in milestones:
        raise RuntimeError("five-minute milestone was requested but not captured")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.output.with_suffix(args.output.suffix + ".tmp")
    temporary.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n")
    temporary.replace(args.output)
    print(json.dumps({"output": str(args.output), **report["summary"]}, indent=2))


if __name__ == "__main__":
    main()
