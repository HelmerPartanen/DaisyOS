#!/usr/bin/env python3
"""Interleaved real C# service/KIO-fallback dispatch benchmark on a private bus."""
import argparse
import importlib.util
import json
import os
import random
import statistics
from pathlib import Path
import subprocess
import sys
import tempfile
import time
from datetime import datetime, timezone

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('performance',ROOT/'scripts/benchmark-os-performance.py')
performance=importlib.util.module_from_spec(spec);spec.loader.exec_module(performance)
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output',type=Path,required=True)
parser.add_argument('--samples',type=int,default=30)
parser.add_argument('--inside',action='store_true',help=argparse.SUPPRESS)
args=parser.parse_args()
if args.samples<30: parser.error('Use at least 30 samples')
args.output=args.output.resolve()
if not args.inside:
    if args.output.exists(): parser.error('Report already exists')
    subprocess.run(['dotnet','build',str(ROOT/'scripts/launch-performance/LaunchPerformance.csproj'),'-c','Release','--disable-build-servers','-m:1','-v','quiet'],check=True)
    with tempfile.TemporaryDirectory(prefix='daisy-launch-benchmark-') as temp:
        env=os.environ.copy();env.update(XDG_DATA_HOME=temp+'/data',XDG_CONFIG_HOME=temp+'/config',XDG_CACHE_HOME=temp+'/cache',
            QT_QPA_PLATFORM='offscreen',DAISYOS_LAUNCH_DEDUPE='0',PATH=str(Path.home()/'.local/bin')+':'+os.environ['PATH'])
        raise SystemExit(subprocess.run(['dbus-run-session','--',sys.executable,__file__,'--inside','--samples',str(args.samples),'--output',str(args.output)],env=env).returncode)
import dbus
root=Path(os.environ['XDG_DATA_HOME']).parent
apps=root/'data/applications';apps.mkdir(parents=True)
(apps/'org.daisyos.dispatchbenchmark.desktop').write_text('[Desktop Entry]\nType=Application\nName=Dispatch benchmark\nExec=/bin/true\n')
subprocess.run(['kbuildsycoca6','--noincremental'],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
with (root/'daemon.log').open('w') as log:
    daemon=subprocess.Popen([str(ROOT/'services/DaisyLaunch/build/daisy-launch')],stdout=log,stderr=subprocess.STDOUT)
    try:
        bus=dbus.SessionBus()
        for _ in range(250):
            if bus.name_has_owner('org.daisyos.Launcher1'):break
            time.sleep(.02)
        else:raise RuntimeError('Daemon not ready')
        rows=root/'rows.json'
        subprocess.run(['dotnet',str(ROOT/'scripts/launch-performance/bin/Release/net10.0/LaunchPerformance.dll'),str(rows),str(args.samples)],check=True,timeout=180)
        raw=json.loads(rows.read_text())
        metrics={backend:performance.summarize([r['elapsedMs'] for r in raw if r['backend']==backend]) for backend in ('service','kio-fallback','legacy-direct')}
        service=[r['trace'] for r in raw if r['backend']=='service']
        for name,start,end in [('registry.lookup','registry_lookup_started','registry_lookup_finished'),('backend.dispatch','dispatch_started','dispatch_accepted'),('daemon.prepare','daemon_received','dispatch_started')]:
            metrics[name]=performance.summarize([(int(t[end])-int(t[start]))/1e6 for t in service])
        metrics['client.non-backend-overhead']=performance.summarize([r['elapsedMs']-(int(r['trace']['dispatch_accepted'])-int(r['trace']['dispatch_started']))/1e6 for r in raw if r['backend']=='service'])
        pss=Path(f'/proc/{daemon.pid}/smaps_rollup').read_text()
        for metric in metrics.values():
            ordered=sorted(metric['rawMs']); metric['p99Ms']=ordered[min(len(ordered)-1, __import__('math').ceil(.99*len(ordered))-1)]
        paired=list(zip(metrics['service']['rawMs'],metrics['kio-fallback']['rawMs']))
        rng=random.Random(1337)
        boot=[]
        for _ in range(2000):
            subset=[rng.choice(paired) for _ in paired]
            boot.append(statistics.median(b-a for a,b in subset))
        boot.sort()
        legacy_pairs=list(zip(metrics['service']['rawMs'],metrics['legacy-direct']['rawMs']))
        rng=random.Random(1337);legacy_boot=[]
        for _ in range(2000): legacy_boot.append(statistics.median(b-a for a,b in [rng.choice(legacy_pairs) for _ in legacy_pairs]))
        legacy_boot.sort()
        stat=Path(f'/proc/{daemon.pid}/stat').read_text().split(') ',1)[1].split()
        report={'protocol' :'daisy-launch-dispatch-v1','createdUtc':datetime.now(timezone.utc).isoformat(),'status':'passed','samples':args.samples,
            'warmups':3,'environment':performance.environment('Release'),'metrics':metrics,'raw':raw, 'daemonMemory':pss,
             'binaryHashes':{'service':performance.file_hash(ROOT/'services/DaisyLaunch/build/daisy-launch'),
                            'fallback':performance.file_hash(Path.home()/'.local/bin/daisy-launch'),
                            'client':performance.file_hash(ROOT/'scripts/launch-performance/bin/Release/net10.0/LaunchPerformance.dll')},
            'daemonCpuSeconds':(int(stat[11])+int(stat[12]))/os.sysconf('SC_CLK_TCK'),
            'nativeVersions':{'qt':performance.command_output(['pkg-config','--modversion','Qt6Core']),'frameworks':performance.command_output(['pacman','-Q','kio','kservice'])},
            'pairedMedianSavingMs':statistics.median(b-a for a,b in paired),
             'pairedMedianSaving95PercentCI':[boot[50],boot[1949]],
            'legacyPairedMedianSavingMs':statistics.median(b-a for a,b in legacy_pairs),
            'legacyPairedMedianSaving95PercentCI':[legacy_boot[50],legacy_boot[1949]],
            'reference':'Original unchanged SafeProcessLauncher direct Exec backend and supported one-shot KIO fallback',
            'workload':'Windowless /bin/true from a disposable desktop entry; actual C# client; interleaved seeded A/B; private bus without systemd; offscreen daemon',
            'timingBoundary':'Client request to backend dispatch acceptance, not application readiness or presentation'}
        args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(report,indent=2)+'\n')
        for name,m in metrics.items():print(f"{name}: median {m['medianMs']:.3f} ms, p95 {m['p95Ms']:.3f} ms")
        print('Report:',args.output)
    finally:
        daemon.terminate();daemon.wait(timeout=5)
