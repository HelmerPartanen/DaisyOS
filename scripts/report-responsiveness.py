#!/usr/bin/env python3
"""Summarize opt-in JSONL timings. A render pass is not compositor presentation."""
import argparse, json, math, collections
parser = argparse.ArgumentParser()
parser.add_argument('trace')
args = parser.parse_args()
groups = collections.defaultdict(list)
with open(args.trace) as source:
    for line in source:
        entry = json.loads(line)
        if entry.get('durationMs') is not None and not entry['stage'].startswith('runtime.'):
            groups[entry['stage']].append(entry['durationMs'])
def percentile(values, fraction):
    return values[max(0, math.ceil(len(values)*fraction)-1)]
print(json.dumps({stage: {'samples':len(values), 'p50Ms':percentile(sorted(values),.5),
    'p95Ms':percentile(sorted(values),.95), 'worstMs':max(values)} for stage, values in groups.items()}, indent=2))
