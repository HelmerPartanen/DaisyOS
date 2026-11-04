#!/usr/bin/env python3
"""Summarize opt-in shell callbacks; never infer compositor presentation or FPS."""
import argparse
import collections
import importlib.util
import json
import math
from pathlib import Path
import statistics


def summarize(entries, refresh_hz=None):
    groups = collections.defaultdict(list)
    for entry in entries:
        value = entry.get('durationMs')
        if value is not None and math.isfinite(value) and value >= 0 and not entry['stage'].startswith('runtime.'):
            groups[entry['stage']].append(value)

    def percentile(values, fraction):
        return values[max(0, math.ceil(len(values) * fraction) - 1)]

    timings = {stage: {'samples': len(values), 'p50Ms': percentile(sorted(values), .5),
                      'p95Ms': percentile(sorted(values), .95), 'worstMs': max(values)}
               for stage, values in groups.items()}
    if refresh_hz is None:
        return timings
    interval = 1000 / refresh_hz
    callbacks = {stage: {'samples': len(values),
                        'overOneAndAHalfRefreshIntervals': sum(value > 1.5 * interval for value in values),
                        'fractionWithinBudget': sum(value <= 1.5 * interval for value in values) / len(values)}
                 for stage, values in groups.items() if stage.endswith('.frame.interval')}
    return {'presentationMeasured': False, 'refreshHz': refresh_hz, 'timings': timings,
            'callbackCadence': callbacks,
            'interpretation': 'UI callback intervals only. Actual presentation and physical input latency require separate capture.'}



def summary(values):
    ordered = sorted(values)
    median = statistics.median(ordered)
    return {"samples": len(ordered), "medianMs": median,
            "p95Ms": ordered[math.ceil(len(ordered) * .95) - 1], "worstMs": ordered[-1],
            "madMs": statistics.median(abs(value - median) for value in ordered)}


def summarize_trace(entries, refresh_hz=None):
    if refresh_hz is not None and (not math.isfinite(refresh_hz) or refresh_hz <= 0):
        raise ValueError("Refresh rate must be positive and finite")
    groups = {}
    counters = {}
    for entry in entries:
        stage = entry["stage"]
        value = entry.get("durationMs")
        if value is None:
            continue
        if not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
            raise ValueError("Invalid timing/counter sample")
        key = stage + ":" + (entry.get("operation") or "")
        if stage.startswith("runtime.gc."):
            counters[key] = value
        else:
            groups.setdefault(key, []).append(value)
    metrics = {key: summary(values) for key, values in groups.items()}
    budget = 1000 / refresh_hz if refresh_hz else None
    for key, values in groups.items():
        if key.startswith("motion.frame.interval:") and budget:
            # This flags delayed UI callbacks, not dropped presented frames.
            metrics[key]["callbackIntervalsOver1_5Budgets"] = sum(value > budget * 1.5 for value in values)
    return {"protocol": 1, "presentationMeasured": False,
            "timingBoundary": "UI event handler to animation callback; frame intervals are UI callbacks, not presentation",
            "refreshHz": refresh_hz, "frameBudgetMs": budget, "metrics": metrics, "lastCounters": counters}


def environment():
    path = Path(__file__).with_name("benchmark-os-performance.py")
    spec = importlib.util.spec_from_file_location("daisy_performance", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    result = module.environment("Release")
    # Include the active renderer and effects, not just the installed KWin version.
    result["compositorSupport"] = module.command_output(["qdbus6", "org.kde.KWin", "/KWin", "supportInformation"])
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trace", type=Path)
    parser.add_argument("--output", type=Path, help="Write an extended report with environment and per-operation timings")
    parser.add_argument("--refresh-hz", type=float, help="Override for the captured output; required for mixed-refresh captures")
    args = parser.parse_args()
    if args.output is None:
        if args.refresh_hz is not None and (not math.isfinite(args.refresh_hz) or args.refresh_hz <= 0):
            parser.error("Refresh rate must be finite and positive")
        print(json.dumps(summarize((json.loads(line) for line in args.trace.read_text().splitlines() if line.strip()), args.refresh_hz), indent=2))
        return
    captured_environment = environment()
    rates = {output["mode"]["refreshRate"] for output in captured_environment["comparable"]["outputs"] if output.get("mode")}
    rate = args.refresh_hz if args.refresh_hz is not None else next(iter(rates)) if len(rates) == 1 else None
    report = summarize_trace((json.loads(line) for line in args.trace.read_text().splitlines() if line.strip()), rate)
    report["environment"] = captured_environment
    report["environmentBoundary"] = "Collected at report time; capture immediately after the trace without changing display settings"
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    main()
