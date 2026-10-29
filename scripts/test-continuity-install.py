#!/usr/bin/env python3
"""Verify immutable upgrades with real payloads in a temporary user prefix."""
import mmap,pathlib,subprocess,sys,tempfile
repo=pathlib.Path(__file__).resolve().parents[1]
payload=pathlib.Path(sys.argv[1]).resolve() if len(sys.argv)>1 else repo/'artifacts/continuity/publish'
with tempfile.TemporaryDirectory(prefix='daisy-continuity-upgrade-') as temporary:
    prefix=pathlib.Path(temporary)/'user prefix'
    subprocess.run([str(repo/'scripts/install-continuity.sh'),str(prefix),str(payload)],check=True,stdout=subprocess.DEVNULL)
    current=prefix/'lib/daisyos/continuity'
    assert current.is_symlink()
    first=current.resolve()
    marker=first/'old-release-only';marker.write_text('retained for already running processes')
    with (first/'DaisyOS.Continuity.dll').open('rb') as stream, mmap.mmap(stream.fileno(),0,access=mmap.ACCESS_READ) as mapped:
        original=mapped[:]
        subprocess.run([str(repo/'scripts/install-continuity.sh'),str(prefix),str(payload)],check=True,stdout=subprocess.DEVNULL)
        assert current.resolve()!=first
        assert mapped[:]==original
        assert marker.exists() and not (current/'old-release-only').exists()
    assert (current/'daisyos-continuity-media').is_file()
    subprocess.run(['systemd-analyze','--user','verify',str(prefix/'share/systemd/user/daisyos-continuity.service')],check=True)
    subprocess.run(['dbus-run-session','--','python3',str(repo/'scripts/test-continuity-daemon.py'),str(prefix/'bin/daisyos-continuity')],check=True)
print('PASS: immutable upgrade preserves mapped assemblies, removes stale payload files, and starts the real daemon')
