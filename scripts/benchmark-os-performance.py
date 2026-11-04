#!/usr/bin/env python3
"""DaisyOS performance protocol v1: fixed workloads, raw timings and a comparable score."""
import argparse
import hashlib
from http.server import BaseHTTPRequestHandler, HTTPServer
import threading
import signal
import json
import math
import os
from pathlib import Path
import platform
import shutil
import socket
import statistics
import subprocess
import sys
import tempfile
import time
import uuid
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[1]
PROTOCOL = "daisyos-performance-v1"
# Fixed reference budgets in milliseconds. A score of 1000 means meeting these
# budgets; this is not a percentile or a claim about other operating systems.
METRICS = {
    "startup.settings": (750.0, 2.0, "Live process launch to initial content rendered"),
    "startup.notes": (500.0, 2.0, "Live process launch to 20 fixture notes rendered"),
    "startup.files": (750.0, 2.0, "Live process launch to 1000 fixture files rendered"),
    "launcher.search": (16.0, 1.0, "Headless Skia search/render, 2000 synthetic apps"),
    "launcher.show-hide": (32.0, 1.0, "Headless Skia hide/show/render, 2000 synthetic apps"),
    "files.enumerate": (40.0, 1.0, "Files model enumeration/preparation, 1000 fixture files"),
    "files.filter": (16.0, 1.0, "Files model filter, no directory re-enumeration"),
    "files.sort": (16.0, 1.0, "Files model size sort, no directory re-enumeration"),
}
FIXTURE = {"version": 1, "files": 1000, "notes": 20, "launcherApps": 2000, "launcherWidth": 720, "launcherHeight": 600,
           "launcherQuery": "Application 012", "filesQuery": "*012*", "filesSort": "Size"}


def summarize(values):
    if not values or any(not isinstance(v, (int, float)) or not math.isfinite(v) or v <= 0 for v in values):
        raise ValueError("Every metric needs positive, finite timing samples")
    ordered = sorted(values)
    median = statistics.median(ordered)
    p95 = ordered[math.ceil(len(ordered) * .95) - 1]
    return {"samples": len(values), "medianMs": median, "p95Ms": p95,
            "minMs": ordered[0], "maxMs": ordered[-1],
            "madMs": statistics.median(abs(v - median) for v in ordered),
            "effectiveMs": .7 * median + .3 * p95, "rawMs": values}


def score(metrics):
    if set(metrics) != set(METRICS):
        raise ValueError("A score requires the complete fixed metric set")
    total = 0.0
    weight_sum = 0.0
    for name, (reference, weight, _) in METRICS.items():
        effective = metrics[name]["effectiveMs"]
        if not math.isfinite(effective) or effective <= 0:
            raise ValueError("Invalid effective latency")
        total += weight * math.log(reference / effective)
        weight_sum += weight
    try:
        result = 1000.0 * math.exp(total / weight_sum)
    except OverflowError as error:
        raise ValueError("Timing samples cannot produce a finite score") from error
    if not math.isfinite(result) or result <= 0:
        raise ValueError("Timing samples cannot produce a finite score")
    return result


def command_output(command):
    try:
        result = subprocess.run(command, capture_output=True, text=True, timeout=5)
        return result.stdout.strip() if result.returncode == 0 else None
    except (OSError, subprocess.TimeoutExpired):
        return None


def read_text(path):
    try:
        return Path(path).read_text().strip()
    except OSError:
        return None


def environment(configuration):
    cpu = read_text("/proc/cpuinfo") or ""
    model = next((line.split(":", 1)[1].strip() for line in cpu.splitlines() if line.startswith("model name")), platform.processor())
    memory = read_text("/proc/meminfo") or ""
    ram = next((line.split(":", 1)[1].strip() for line in memory.splitlines() if line.startswith("MemTotal:")), None)
    display = command_output(["kscreen-doctor", "-j"])
    outputs = []
    if display:
        try:
            for output in json.loads(display).get("outputs", []):
                if output.get("enabled"):
                    current = next((mode for mode in output.get("modes", []) if str(mode.get("id")) == str(output.get("currentModeId"))), None)
                    outputs.append({"name": output.get("name"), "mode": current, "scale": output.get("scale"),
                                    "hdr": output.get("hdr"), "wcg": output.get("wcg")})
        except (ValueError, TypeError):
            pass
    gpu = sorted(p.read_text().strip() for p in Path("/sys/class/drm").glob("card*/device/uevent"))
    comparable = {"host": socket.gethostname(), "cpu": model, "cpuCount": os.cpu_count(), "ram": ram,
                  "kernel": platform.release(), "configuration": configuration, "dotnet": command_output(["dotnet", "--version"]),
                  "kwin": command_output(["kwin_wayland", "--version"]), "outputs": outputs, "gpu": gpu,
                  "governor": read_text("/sys/devices/system/cpu/cpu0/cpufreq/scaling_governor"),
                  "powerProfile": command_output(["powerprofilesctl", "get"])}
    return {"comparable": comparable, "loadAverage": list(os.getloadavg()), "os": platform.platform(),
            "backend": "Wayland" if os.environ.get("WAYLAND_DISPLAY") else "X11"}


def fixture_filesystem(path):
    target = str(Path(path).resolve())
    candidates = []
    for line in (read_text("/proc/self/mountinfo") or "").splitlines():
        before, separator, after = line.partition(" - ")
        if not separator: continue
        fields, filesystem = before.split(), after.split()
        if len(fields) < 5 or len(filesystem) < 2: continue
        mount = fields[4].replace("\\040", " ")
        if target == mount or target.startswith(mount.rstrip("/") + "/"):
            candidates.append((len(mount), {"type": filesystem[0], "mount": mount, "source": filesystem[1]}))
    return max(candidates, key=lambda item: item[0])[1] if candidates else None


def file_hash(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def fixtures(root):
    (root / "files").mkdir()
    (root / "notes").mkdir()
    for i in range(1000):
        (root / "files" / f"entry-{i:04d}.txt").write_bytes((f"DaisyOS fixture {i:04d}\n" * (1 + i % 16)).encode())
    for i in range(20):
        (root / "notes" / f"note-{i:02d}.txt").write_text(f"Benchmark note {i:02d}\n" + "Repeatable note text.\n" * 50)
    for directory in (root / "files", root / "notes"):
        for path in directory.iterdir():
            os.utime(path, (1577836800, 1577836800))
    digest = hashlib.sha256(json.dumps(FIXTURE, sort_keys=True).encode())
    for path in sorted(p for directory in (root / "files", root / "notes") for p in directory.iterdir()):
        digest.update(str(path.relative_to(root)).encode() + b"\0" + path.read_bytes() + b"\0")
    return digest.hexdigest()


def run_checked(command, log):
    with log.open("w") as output:
        result = subprocess.run(command, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT)
    if result.returncode:
        raise RuntimeError(f"Command failed; see {log}")


def validate_signal(signal, process_id, nonce, app):
    if signal.get("protocol") != 1 or signal.get("token") != nonce or signal.get("processId") != process_id or signal.get("application") != app:
        raise ValueError(f"{app} returned an invalid readiness handshake")
    if signal.get("stage") != "initial-content-rendered":
        raise ValueError(f"{app} readiness failed: {signal.get('error')}")


def startup(binary, app, scratch, nonce, log, timeout):
    result_path = scratch / (nonce + ".json")
    env = os.environ.copy()
    env.update({"DAISYOS_PERFORMANCE_RESULT": str(result_path), "DAISYOS_PERFORMANCE_TOKEN": nonce,
                "DAISYOS_PERFORMANCE_DATA": str(scratch), "XDG_CONFIG_HOME": str(scratch / "config"),
                "XDG_STATE_HOME": str(scratch / "state"), "XDG_CACHE_HOME": str(scratch / "cache")})
    arguments = [str(binary)] + ([str(scratch / "files")] if app == "files" else [])
    started = time.perf_counter_ns()
    process = None
    try:
        with log.open("w") as output:
            process = subprocess.Popen(arguments, env=env, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
            while not result_path.exists():
                if process.poll() is not None:
                    raise RuntimeError(f"{app} exited before readiness (code {process.returncode}); see {log}")
                if (time.perf_counter_ns() - started) / 1e9 > timeout:
                    raise TimeoutError(f"{app} startup timed out; see {log}")
                time.sleep(.002)
            elapsed = (time.perf_counter_ns() - started) / 1e6
            signal = json.loads(result_path.read_text())
            validate_signal(signal, process.pid, nonce, app)
            process.wait(timeout=5)
            if process.returncode != 0:
                raise RuntimeError(f"{app} failed during shutdown (code {process.returncode}); see {log}")
            return elapsed
    finally:
        # Only terminate the exact child we created; never the user's open apps.
        if process is not None and process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait()


def chrome_startup(binary, scratch, log, timeout):
    """Visible Chrome, fresh isolated profile, local page's first-contentful-paint callback."""
    nonce = uuid.uuid4().hex
    ready = threading.Event()
    received = []
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *args): pass
        def do_GET(self):
            if self.path == "/ready/" + nonce:
                received.append(time.perf_counter_ns())
                ready.set()
                self.send_response(204); self.end_headers()
            elif self.path == "/" + nonce:
                body = ("<!doctype html><meta charset=utf-8><title>Chrome startup benchmark</title>"
                        "<style>body{font:24px sans-serif;padding:40px;background:#fafafa;color:#222}</style>"
                        "<h1>DaisyOS startup benchmark</h1><p>Local content. No external resources.</p>"
                        "<script>new PerformanceObserver((list,observer)=>{"
                        "if(list.getEntries().some(e=>e.name==='first-contentful-paint')){"
                        "observer.disconnect();fetch('/ready/" + nonce + "');}})"
                        ".observe({type:'paint',buffered:true});</script>").encode()
                self.send_response(200); self.send_header("Content-Type", "text/html; charset=utf-8")
                self.send_header("Content-Length", str(len(body))); self.end_headers(); self.wfile.write(body)
            else:
                self.send_response(404); self.end_headers()
    server = HTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
    process = None
    try:
        with tempfile.TemporaryDirectory(prefix="chrome-", dir=scratch) as profile, log.open("w") as output:
            arguments = [str(binary), "--user-data-dir=" + profile, "--no-first-run",
                         "--no-default-browser-check", "--disable-background-networking", "--disable-sync",
                         "--new-window", f"http://127.0.0.1:{server.server_port}/{nonce}"]
            started = time.perf_counter_ns()
            process = subprocess.Popen(arguments, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
            try:
                while not ready.wait(.002):
                    if process.poll() is not None:
                        raise RuntimeError(f"Chrome exited before content paint; see {log}")
                    if (time.perf_counter_ns() - started) / 1e9 > timeout:
                        raise TimeoutError(f"Chrome startup timed out; see {log}")
                return (received[0] - started) / 1e6
            finally:
                # Dedicated process group and profile: never close the user's Chrome.
                if process.poll() is None:
                    os.killpg(process.pid, signal.SIGTERM)
                    try: process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        os.killpg(process.pid, signal.SIGKILL); process.wait()
    finally:
        server.shutdown(); server.server_close(); thread.join()


def measure_chrome(args, scratch, logs):
    binary = args.chrome_binary or shutil.which("google-chrome-stable") or shutil.which("google-chrome")
    if not binary:
        raise RuntimeError("Google Chrome not found; specify --chrome-binary or explicitly use --skip-chrome")
    values = []
    for run in range(-args.warmups, args.samples):
        print(f"chrome startup: {'warmup' if run < 0 else str(run + 1) + '/' + str(args.samples)}", flush=True)
        value = chrome_startup(binary, scratch, logs / f"chrome-{run}.log", args.timeout)
        if run >= 0: values.append(value)
    return summarize(values) | {"protocol": "chrome-local-fcp-v1", "version": command_output([str(binary), "--version"]),
                               "description": "Process launch to local first-contentful-paint callback; includes loopback IPC. Fresh profile per launch; warmed OS caches. Not physical presentation."}


def make_report(args):
    if not (os.environ.get("WAYLAND_DISPLAY") or os.environ.get("DISPLAY")):
        raise RuntimeError("Run this benchmark inside the DaisyOS graphical session")
    if not sys.platform.startswith("linux"):
        raise RuntimeError("The live benchmark currently supports Linux")
    args.output = args.output.resolve()
    logs = args.output.with_suffix(".logs")
    logs.mkdir(parents=True, exist_ok=True)
    if args.output.exists():
        raise RuntimeError(f"Report already exists: {args.output}; choose a new output filename")
    if args.build:
        for app in ("Files", "Notes", "Settings"):
            print(f"Building {app} ({args.configuration})…", flush=True)
            run_checked(["dotnet", "build", f"src/DaisyOS.{app}/DaisyOS.{app}.csproj", "-c", args.configuration,
                         "--no-restore", "--disable-build-servers", "-m:1", "-v", "quiet"], logs / f"build-{app}.log")
        run_checked(["dotnet", "build", "external/Avalonia/src/Headless/Avalonia.Headless/Avalonia.Headless.csproj",
                     "-c", args.configuration, "--no-restore", "--disable-build-servers", "-m:1", "-v", "quiet"], logs / "build-headless.log")
        run_checked(["dotnet", "build", "scripts/os-performance/PerformanceBenchmark.csproj", "-c", args.configuration,
                     "--disable-build-servers", "-m:1", "-v", "quiet"], logs / "build-harness.log")
    binaries = {app.lower(): ROOT / f"src/DaisyOS.{app}/bin/{args.configuration}/net10.0/DaisyOS.{app}" for app in ("Files", "Notes", "Settings")}
    harness = ROOT / f"scripts/os-performance/bin/{args.configuration}/net10.0/DaisyOS.PerformanceBenchmark.dll"
    for binary in [*binaries.values(), harness]:
        if not binary.exists():
            raise RuntimeError(f"Missing binary: {binary}; run with --build")
    shared_hash = file_hash(binaries["settings"].parent / "DaisyOS.Shell.dll")
    for directory in [binary.parent for binary in binaries.values()] + [harness.parent]:
        if file_hash(directory / "DaisyOS.Shell.dll") != shared_hash:
            raise RuntimeError("App/harness shared assemblies are out of sync; rebuild with --build")
    report = {"protocol": PROTOCOL, "createdUtc": datetime.now(timezone.utc).isoformat(), "status": "running", "score": None,
              "samples": args.samples, "warmups": args.warmups, "fixture": FIXTURE, "environment": environment(args.configuration),
              "timingBoundary": "Parent process launch to authenticated initial-content-rendered signal; includes <=2 ms polling plus signal IO. Render completion is not compositor presentation.",
              "cachePolicy": "Fresh processes, warmed filesystem caches; no system cache flush", "metrics": {}, "failures": [],
              "binaryHashes": {app: file_hash(binary.parent / (binary.name + ".dll")) for app, binary in binaries.items()}}
    report["binaryHashes"]["shell"] = file_hash(binaries["settings"].parent / "DaisyOS.Shell.dll")
    report["runtimeHashes"] = {path.name: file_hash(path) for path in sorted(binaries["settings"].parent.glob("*.dll"))}
    with tempfile.TemporaryDirectory(prefix="daisyos-performance-") as temp:
        scratch = Path(temp)
        report["fixtureHash"] = fixtures(scratch)
        report["environment"]["comparable"]["fixtureFilesystem"] = fixture_filesystem(scratch)
        # Copy appearance preferences into an isolated config; never write user settings or notes.
        source_config = Path(os.environ.get("XDG_CONFIG_HOME", str(Path.home() / ".config"))) / "DaisyOS"
        config = scratch / "config" / "DaisyOS"
        config.mkdir(parents=True)
        appearance = source_config / "appearance.json"
        appearance_settings = {}
        if appearance.exists():
            shutil.copy2(appearance, config / "appearance.json")
            appearance_settings = json.loads(appearance.read_text())
        report["environment"]["comparable"]["appearance"] = appearance_settings
        report["thirdPartyMetrics"] = {}
        if not args.skip_chrome:
            try:
                report["thirdPartyMetrics"]["startup.chrome"] = measure_chrome(args, scratch, logs)
            except (RuntimeError, OSError, TimeoutError, subprocess.TimeoutExpired, ValueError) as error:
                report["failures"].append(str(error))
        raw = {}
        report["startupFirstLaunch"] = {}
        report["startupFirstLaunchBoundary"] = (
            "First fresh process per app in this run, before discarded warmups. "
            "Filesystem/driver caches are uncontrolled; this is not verified disk-cold startup.")
        for app, binary in binaries.items():
            values = []
            for run in range(-args.warmups, args.samples):
                print(f"{app} startup: {'warmup' if run < 0 else str(run + 1) + '/' + str(args.samples)}", flush=True)
                try:
                    value = startup(binary, app, scratch, uuid.uuid4().hex, logs / f"{app}-{run}.log", args.timeout)
                    if run == -args.warmups:
                        report["startupFirstLaunch"][app] = {
                            "elapsedMs": value, "includedInSummary": args.warmups == 0,
                            "cacheStateVerified": False}
                    if run >= 0:
                        values.append(value)
                except (RuntimeError, OSError, TimeoutError, subprocess.TimeoutExpired, ValueError) as error:
                    report["failures"].append(str(error))
                    break
            if len(values) == args.samples:
                raw["startup." + app] = values
        result = scratch / "headless.json"
        try:
            print("Running deterministic launcher and Files workloads…", flush=True)
            headless_env = os.environ.copy()
            headless_env.update({"XDG_CONFIG_HOME": str(scratch / "config"), "XDG_STATE_HOME": str(scratch / "state")})
            with (logs / "headless.log").open("w") as log:
                completed = subprocess.run(["dotnet", str(harness), str(scratch), str(result), str(args.samples), str(args.warmups)],
                                           env=headless_env, stdout=log, stderr=subprocess.STDOUT, timeout=120)
            if completed.returncode != 0:
                raise RuntimeError(f"Deterministic workloads failed; see {logs / 'headless.log'}")
            data = json.loads(result.read_text())
            if data.get("workload") != {key: value for key, value in FIXTURE.items() if key not in ("version", "notes")}:
                raise ValueError("Deterministic workload specification does not match protocol")
            if data.get("protocol") != 1 or set(data["metrics"]) != set(METRICS) - {"startup." + app for app in binaries}:
                raise ValueError("Incorrect deterministic metric set")
            for name, values in data["metrics"].items():
                if len(values) != args.samples:
                    raise ValueError("Incorrect sample count")
                raw[name] = values
        except (RuntimeError, OSError, ValueError, subprocess.TimeoutExpired) as error:
            report["failures"].append(str(error))
        for name, values in raw.items():
            try:
                report["metrics"][name] = summarize(values) | {"referenceMs": METRICS[name][0], "weight": METRICS[name][1], "description": METRICS[name][2]}
            except ValueError as error:
                report["failures"].append(f"{name}: {error}")
        if not report["failures"] and set(report["metrics"]) == set(METRICS):
            report["score"] = score(report["metrics"])
            report["status"] = "passed"
        else:
            report["status"] = "failed"
    args.output.write_text(json.dumps(report, indent=2) + "\n")
    print_report(report)
    print(f"Report: {args.output}\nLogs: {logs}")
    return 0 if report["status"] == "passed" else 1


def print_report(report):
    print(f"\nDaisyOS performance score: {report['score']:.1f} (higher is better)" if report.get("score") is not None else "\nBenchmark failed: no score")
    print(f"{'Workload':24} {'Median ms':>12} {'p95 ms':>12} {'MAD ms':>12}")
    for name, metric in (report["metrics"] | report.get("thirdPartyMetrics", {})).items():
        print(f"{name:24} {metric['medianMs']:12.3f} {metric['p95Ms']:12.3f} {metric['madMs']:12.3f}")
    for failure in report.get("failures", []):
        print(f"FAIL: {failure}")


def compare_reports(before, after, allow_environment=False):
    if before.get("status") != "passed" or after.get("status") != "passed":
        raise ValueError("Only successful, complete reports can be compared")
    for field in ("protocol", "fixtureHash", "fixture", "samples", "warmups", "cachePolicy"):
        if before.get(field) != after.get(field):
            raise ValueError(f"Incompatible benchmark {field}")
    if before.get("protocol") != PROTOCOL:
        raise ValueError("Unsupported scoring protocol")
    if before.get("fixture") != FIXTURE:
        raise ValueError("Fixture specification does not match the protocol")
    a, b = before["environment"]["comparable"], after["environment"]["comparable"]
    differences = sorted(key for key in set(a) | set(b) if a.get(key) != b.get(key))
    if differences and not allow_environment:
        raise ValueError("Environment differs: " + ", ".join(differences) + "; use --allow-environment-differences for an explicitly qualified comparison")
    # Recompute scores from raw samples so changed weights or summary fields cannot
    # quietly make an old result look better under the same protocol number.
    def verified(report):
        for name, value in report["metrics"].items():
            if name not in METRICS or (value.get("referenceMs"), value.get("weight")) != METRICS[name][:2]:
                raise ValueError("Scoring references/weights differ from the protocol")
        metrics = {name: summarize(value["rawMs"]) for name, value in report["metrics"].items()}
        if any(value["samples"] != report["samples"] for value in metrics.values()):
            raise ValueError("Incorrect stored sample counts")
        return metrics, score(metrics)
    old, old_score = verified(before)
    new, new_score = verified(after)
    print(f"Score: {old_score:.1f} → {new_score:.1f} ({(new_score / old_score - 1) * 100:+.1f}%)")
    print(f"{'Workload':24} {'Median change':>16} {'p95 change':>16}")
    for name in METRICS:
        print(f"{name:24} {(new[name]['medianMs'] / old[name]['medianMs'] - 1) * 100:+15.1f}% {(new[name]['p95Ms'] / old[name]['p95Ms'] - 1) * 100:+15.1f}%")
    old_chrome = before.get("thirdPartyMetrics", {}).get("startup.chrome")
    new_chrome = after.get("thirdPartyMetrics", {}).get("startup.chrome")
    if old_chrome and new_chrome:
        if (old_chrome.get("protocol"), old_chrome.get("version")) != (new_chrome.get("protocol"), new_chrome.get("version")):
            print("Chrome comparison unavailable: browser version or workload changed.")
        else:
            a_chrome, b_chrome = summarize(old_chrome["rawMs"]), summarize(new_chrome["rawMs"])
            if any(m["samples"] != r["samples"] for m, r in ((a_chrome, before), (b_chrome, after))):
                raise ValueError("Incorrect Chrome sample count")
            print(f"{'startup.chrome':24} {(b_chrome['medianMs'] / a_chrome['medianMs'] - 1) * 100:+15.1f}% {(b_chrome['p95Ms'] / a_chrome['p95Ms'] - 1) * 100:+15.1f}%")
    elif old_chrome or new_chrome:
        print("Chrome has no matching result in the other report; establish a Chrome baseline first.")
    print("Latency changes: negative is faster. Score changes: positive is faster.")
    if differences:
        print("QUALIFIED COMPARISON: environment differs: " + ", ".join(differences))
    return new_score / old_score


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    run = commands.add_parser("run")
    run.add_argument("--configuration", choices=["Debug", "Release"], default="Release")
    run.add_argument("--samples", type=int, default=7)
    run.add_argument("--warmups", type=int, default=2)
    run.add_argument("--timeout", type=float, default=40)
    run.add_argument("--chrome-binary", help="Path to Google Chrome executable")
    run.add_argument("--skip-chrome", action="store_true", help="Explicitly omit third-party startup timing")
    run.add_argument("--build", action="store_true", help="Build apps and harness before timing begins")
    run.add_argument("--output", type=Path, required=True)
    compare = commands.add_parser("compare")
    compare.add_argument("before", type=Path)
    compare.add_argument("after", type=Path)
    compare.add_argument("--allow-environment-differences", action="store_true")
    args = parser.parse_args()
    try:
        if args.command == "run":
            if args.samples < 5 or args.warmups < 1 or not math.isfinite(args.timeout) or args.timeout <= 0:
                raise ValueError("Use at least 5 samples, 1 warmup and a positive finite timeout")
            return make_report(args)
        compare_reports(json.loads(args.before.read_text()), json.loads(args.after.read_text()), args.allow_environment_differences)
        return 0
    except (ValueError, RuntimeError, OSError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
