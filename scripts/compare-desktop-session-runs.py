#!/usr/bin/env python3
"""Compare three or more matching five-minute DaisyOS session captures."""

import argparse
import json
from pathlib import Path
import statistics
import sys


PROTOCOL = "daisyos-desktop-session-v1"
REQUIRED_ENVIRONMENT = ("host", "kernel", "machine", "sessionType", "desktop", "selection")
METRICS = {
    "desktopProcessesPssMiB": lambda sample: sample["desktopProcessesPssBytes"] / (1024 * 1024),
    "desktopProcessesPrivateMiB": lambda sample: sample["desktopProcessesPrivateBytes"] / (1024 * 1024),
    "desktopCpuPercentOfOneCore": lambda sample: sum(
        row["cpuPercentOfOneCore"] or 0 for row in sample["desktopProcesses"]),
    "systemUsedExcludingAvailableMiB": lambda sample: sample["memory"]["usedExcludingAvailableBytes"] / (1024 * 1024),
    "memAvailableMiB": lambda sample: sample["memory"]["availableBytes"] / (1024 * 1024),
}


def compare(paths):
    if len(paths) < 3:
        raise ValueError("at least three fresh-login reports are required")
    reports = [json.loads(Path(path).read_text()) for path in paths]
    for report in reports:
        if report.get("metadata", {}).get("protocol") != PROTOCOL:
            raise ValueError("all reports must use daisyos-desktop-session-v1")
        if report.get("workload", {}).get("state") != "fresh-login-idle":
            raise ValueError("each report must be annotated with workload.state=fresh-login-idle")
    baseline_metadata = reports[0]["metadata"]
    for report in reports[1:]:
        for key in REQUIRED_ENVIRONMENT:
            if report["metadata"].get(key) != baseline_metadata.get(key):
                raise ValueError(f"capture environments differ at metadata.{key}")
        if report["workload"].get("annotations") != reports[0]["workload"].get("annotations"):
            raise ValueError("workload annotations differ between captures")
    selected = []
    complete = True
    for index, report in enumerate(reports, 1):
        samples = [sample for sample in report.get("samples", [])
                   if sample.get("elapsedSeconds", 0) >= 300]
        if not samples:
            raise ValueError(f"capture {index} has no sample at or after five minutes")
        sample = samples[-1]
        complete = complete and sample.get("desktopProcessesPssComplete") is True
        selected.append({"path": str(paths[index - 1]), "sample": sample,
                         "readinessLatencyMs": report.get("summary", {}).get("readinessLatencyMs")})
    metrics = {}
    for name, getter in METRICS.items():
        values = [getter(entry["sample"]) for entry in selected]
        median = statistics.median(values)
        metrics[name] = {"median": median,
                         "mad": statistics.median(abs(value - median) for value in values),
                         "min": min(values), "max": max(values), "values": values}
    readiness = [entry["readinessLatencyMs"] for entry in selected]
    readiness = [value for value in readiness if value is not None]
    if readiness:
        median = statistics.median(readiness)
        metrics["shellReadinessMs"] = {"median": median,
                                       "mad": statistics.median(abs(value - median) for value in readiness),
                                       "min": min(readiness), "max": max(readiness), "values": readiness}
    median_pss = metrics["desktopProcessesPssMiB"]["median"]
    return {"protocol": PROTOCOL, "captureCount": len(reports),
            "acceptanceEligible": complete,
            "acceptanceReason": "PSS coverage complete across all selected desktop processes." if complete else
                "PSS is unavailable for one or more selected processes; do not treat the subtotal as the whole-session target.",
            "idlePssBudgetMiB": 700, "idlePssMedianMiB": median_pss,
            "idlePssBudgetPassed": complete and median_pss <= 700,
            "metrics": metrics,
            "captures": [{"path": entry["path"],
                          "unreadablePssProcesses": entry["sample"].get("processesWithoutReadableSmaps", 0)}
                         for entry in selected]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reports", nargs="+", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        result = compare(args.reports)
    except (OSError, ValueError, KeyError, json.JSONDecodeError) as error:
        parser.error(str(error))
    rendered = json.dumps(result, indent=2, sort_keys=True) + "\n"
    if args.output:
        if args.output.exists():
            parser.error(f"refusing to overwrite existing report: {args.output}")
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered)
    print(rendered, end="")
    return 0 if result["acceptanceEligible"] and result["idlePssBudgetPassed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
