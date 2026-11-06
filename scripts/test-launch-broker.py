#!/usr/bin/env python3
"""Rust broker failure, resource-limit and subprocess-lifetime tests on a private bus."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
if '--inside' not in sys.argv:
    binary = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / 'services/DaisyLaunch/build/daisy-launch'
    with tempfile.TemporaryDirectory(prefix='daisy-broker-test-') as temp:
        root = Path(temp)
        shutil.copy2(binary, root / 'daisy-launch')
        (root / 'daisy-launch-backend').write_text('''#!/usr/bin/env python3
import json,os,pathlib,sys,time
if sys.argv[1] == '--catalog': print('[]'); sys.exit(0)
if sys.argv[1] != '--broker-launch': sys.exit(2)
r=json.load(sys.stdin)
assert 'secret-token' not in pathlib.Path('/proc/self/cmdline').read_text()
pathlib.Path(os.environ['FIXTURE_ROOT'],'child').write_text(str(os.getpid()))
if r['app']=='hang.desktop': time.sleep(60)
if r['app']=='malformed.desktop': print('{'); sys.exit(1)
if r['app']=='crash.desktop': sys.exit(1)
print(json.dumps({'error':''}))
''')
        (root / 'daisy-launch-backend').chmod(0o755)
        env = os.environ.copy()
        env.update(XDG_DATA_HOME=temp+'/data', XDG_DATA_DIRS=temp+'/system', XDG_CACHE_HOME=temp+'/cache', XDG_CONFIG_HOME=temp+'/config', FIXTURE_ROOT=temp)
        raise SystemExit(subprocess.run(['dbus-run-session', '--', sys.executable, __file__, '--inside', temp], env=env).returncode)
import dbus
root = Path(sys.argv[-1])
bus = dbus.SessionBus()
p = subprocess.Popen([str(root / 'daisy-launch')], stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)

def rejected(operation, code):
    try:
        operation()
    except dbus.DBusException as error:
        assert error.get_dbus_name() == 'org.daisyos.Launcher1.' + code, str(error)
    else:
        raise AssertionError('Expected ' + code)


def gone(pid):
    end = time.monotonic() + 3
    while time.monotonic() < end:
        if not Path(f'/proc/{pid}').exists(): return
        time.sleep(.01)
    raise AssertionError('Backend survived cancellation/shutdown')


try:
    end = time.monotonic() + 5
    while not bus.name_has_owner('org.daisyos.Launcher1'):
        if p.poll() is not None or time.monotonic() > end: raise AssertionError('Broker startup failed')
        time.sleep(.01)
    api = dbus.Interface(bus.get_object('org.daisyos.Launcher1', '/org/daisyos/Launcher1'), 'org.daisyos.Launcher1')
    def launch(app='ok.desktop', id=None, token='secret-token', options=None):
        id = id or uuid.uuid4().hex
        assert api.Launch(id, app, '', dbus.Array([], signature='s'), token, dbus.Dictionary(options or {}, signature='sv'))
        return id
    rejected(lambda: launch(id='invalid'), 'InvalidArgument')
    rejected(lambda: launch(token='x'*4097), 'InvalidArgument')
    rejected(lambda: launch(options={'unsupported':True}), 'InvalidArgument')
    first = launch(); assert api.Await(first)
    original_pid = (root / 'child').read_text()
    launch(id=first, token='replacement')
    time.sleep(.05)
    assert (root / 'child').read_text() == original_pid, 'Idempotent launch spawned another backend'
    rejected(lambda: launch(app='different.desktop', id=first), 'InvalidArgument')
    assert 'secret-token' not in api.Trace(first)
    for app in ['malformed.desktop', 'crash.desktop']:
        request = launch(app)
        rejected(lambda: api.Await(request), 'BackendFailed')
    before_hang = (root / 'child').read_text()
    request = launch('hang.desktop')
    end = time.monotonic()+3
    while (root / 'child').read_text() == before_hang:
        if time.monotonic()>end: raise AssertionError('Backend did not start')
        time.sleep(.01)
    time.sleep(.1)
    child = int((root / 'child').read_text())
    api.Cancel(request)
    rejected(lambda: api.Await(request), 'Cancelled')
    gone(child)
    time.sleep(1.1)  # independent of per-owner rate limits from earlier checks
    pending = [launch('hang.desktop') for _ in range(16)]
    rejected(lambda: launch('hang.desktop'), 'RateLimited')
    time.sleep(.1)
    assert len(Path(f'/proc/{p.pid}/task/{p.pid}/children').read_text().split()) == 1, 'Pending burst loaded multiple helpers'
    for request in pending: api.Cancel(request)
    time.sleep(1.1)
    request = launch('hang.desktop')
    rejected(lambda: api.Await(request, timeout=18), 'TimedOut')
    time.sleep(.1)
    assert not Path(f'/proc/{p.pid}/task/{p.pid}/children').read_text().strip(), 'Backend was not reaped'
    # Missing helper must fail the accepted request without retrying.
    helper = root / 'daisy-launch-backend'
    helper.rename(root / 'hidden-backend')
    request = launch()
    rejected(lambda: api.Await(request), 'BackendFailed')
    (root / 'hidden-backend').rename(helper)
    # Completed request identities also have an aggregate byte budget, so a
    # stream of maximum-size URI lists cannot retain hundreds of MiB for 2 min.
    uri='https://example.invalid/'+'x'*(8192-len('https://example.invalid/'))
    large_uris=dbus.Array([uri]*64,signature='s')
    accepted=0
    for _ in range(20):
        try:
            request=uuid.uuid4().hex
            api.Launch(request,'ok.desktop','',large_uris,'',dbus.Dictionary({},signature='sv'))
        except dbus.DBusException as error:
            assert error.get_dbus_name().endswith('.RateLimited')
            break
        assert api.Await(request)
        accepted+=1
        time.sleep(.07)
    else: raise AssertionError('Request metadata budget was not enforced')
    assert 10<=accepted<20, accepted
    request = launch('hang.desktop')
    time.sleep(.2)
    child = int((root / 'child').read_text())
    p.terminate(); p.wait(timeout=3)
    gone(child)
    print('PASS: validation, idempotence/conflicts, token pipe, malformed/crashed/missing helper, cancellation/reaping, pending limit, timeout, shutdown cleanup')
finally:
    if p.poll() is None: p.terminate(); p.wait(timeout=3)
