#!/usr/bin/env python3
"""Read-only resource soak of an existing shell. Does not measure visible latency."""
import argparse, json, os, time
p=argparse.ArgumentParser();p.add_argument('pid',type=int);p.add_argument('output');p.add_argument('seconds',type=int);a=p.parse_args()
hz=os.sysconf('SC_CLK_TCK');start=time.monotonic();last=None
with open(a.output,'w') as out:
    while time.monotonic()-start<a.seconds:
        stat=open(f'/proc/{a.pid}/stat').read().rsplit(')',1)[1].split()
        ticks=int(stat[11])+int(stat[12]);now=time.monotonic()
        mem={line.split(':')[0]:line.split(':')[1].strip() for line in open(f'/proc/{a.pid}/status') if line.startswith(('VmRSS:','VmSize:','Threads:'))}
        try:
            for line in open(f'/proc/{a.pid}/smaps_rollup'):
                if line.startswith(('Pss:', 'Private_Clean:', 'Private_Dirty:', 'SwapPss:')):
                    mem[line.split(':')[0]] = line.split(':')[1].strip()
        except (OSError, PermissionError):
            pass
        row={'elapsedSeconds':now-start,'cpuPercentOfOneCore':None if last is None else 100*(ticks-last[1])/hz/(now-last[0]),'memory':mem,'presentationMeasured':False}
        out.write(json.dumps(row)+'\n');out.flush();last=(now,ticks)
        time.sleep(min(5,max(0,a.seconds-(time.monotonic()-start))))
