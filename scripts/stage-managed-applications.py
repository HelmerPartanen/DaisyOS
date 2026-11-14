#!/usr/bin/env python3
"""Build ownership-accurate bootstrap packages for the existing ISO payload.

Archives contain the actual staged files, not synthetic package-name masks.
Only the image's first boot registers them; this script never touches the host DB.
"""
import argparse, os, pathlib, subprocess, tempfile, time, tarfile
parser=argparse.ArgumentParser()
parser.add_argument('--root',required=True,type=pathlib.Path)
parser.add_argument('--payload',default='/opt/DaisyOS/shell')
parser.add_argument('--version',default='0.1.0')
args=parser.parse_args(); root=args.root.resolve(); payload=root/args.payload.lstrip('/')
if not payload.is_dir(): raise SystemExit('Missing staged shell payload')
if not all(c.isalnum() or c in '.+_' for c in args.version): raise SystemExit('Invalid package version')
optional=('Files','Mail','Notes','Calculator','AppStore','DiskManager','UIKit')
cache=root/'var/cache/daisyos/bootstrap-packages';cache.mkdir(parents=True,exist_ok=True)

def contents(path):
    if not path.exists() and not path.is_symlink():return set()
    return {path}|(set(path.rglob('*')) if path.is_dir() and not path.is_symlink() else set())

service=root/'usr/lib/systemd/system/daisyos-register-applications.service';service.parent.mkdir(parents=True,exist_ok=True)
service.write_text('''[Unit]
Description=Register DaisyOS application package ownership
Before=display-manager.service
After=local-fs.target
ConditionPathExists=!/var/lib/daisyos/application-packages-registered

[Service]
Type=oneshot
ExecStart=/usr/lib/daisyos/register-application-packages
RemainAfterExit=yes

[Install]
WantedBy=multi-user.target
''')
register=root/'usr/lib/daisyos/register-application-packages';register.parent.mkdir(parents=True,exist_ok=True)
register.write_text('''#!/usr/bin/env bash
set -euo pipefail
packages=(/var/cache/daisyos/bootstrap-packages/*.pkg.tar.zst)
[[ -f "${packages[0]}" ]] || { echo 'Missing bootstrap application packages' >&2; exit 1; }
# Payload is already staged. Register precisely the same files with Pacman.
pacman -U --dbonly --noconfirm -- "${packages[@]}"
if command -v update-mime-database >/dev/null 2>&1; then update-mime-database /usr/share/mime; fi
if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database /usr/share/applications; fi
install -d /var/lib/daisyos
touch /var/lib/daisyos/application-packages-registered
''');register.chmod(0o755)
wants=root/'etc/systemd/system/multi-user.target.wants';wants.mkdir(parents=True,exist_ok=True)
link=wants/service.name
if link.is_symlink() or link.exists():link.unlink()
link.symlink_to('/usr/lib/systemd/system/'+service.name)

ownership={}
for app in optional:
    directory=payload/'apps'/app
    if not (directory/('DaisyOS.'+app)).is_file():raise SystemExit('Missing optional payload: '+app)
    paths=contents(directory)
    for prefix in ('usr/bin','usr/local/bin'):
        paths|=contents(root/prefix/('daisyos-'+app.lower()))
    paths|=contents(root/'usr/share/applications'/('org.daisyos.'+app.lower()+'.desktop'))
    icons=root/'usr/share/icons'
    if icons.exists():
        for icon in icons.rglob('org.daisyos.'+app.lower()+'.*'):paths|=contents(icon)
    if app=='Files':paths|=contents(root/'usr/share/dbus-1/services/org.freedesktop.FileManager1.service')
    if app=='Mail':
        for relative in ('usr/lib/systemd/user/daisyos-mail-check.service','usr/lib/systemd/user/daisyos-mail-check.timer','usr/lib/systemd/user/timers.target.wants/daisyos-mail-check.timer'):
            paths|=contents(root/relative)
    ownership['daisyos-'+app.lower()]=paths
core=contents(payload)
for relative in ('usr/lib/daisyos','usr/share/daisyos','usr/share/polkit-1/actions/org.daisyos.applications.policy','usr/lib/systemd/system/daisyos-register-applications.service','etc/systemd/system/multi-user.target.wants/daisyos-register-applications.service','usr/share/mime/packages/org.daisyos.applications.xml'):
    core|=contents(root/relative)
for directory in ('usr/bin','usr/local/bin','usr/share/applications','usr/share/dbus-1/services','usr/lib/systemd/user'):
    path=root/directory
    if path.is_dir():
        for entry in path.rglob('*'):
            if entry.name.startswith(('daisyos-','org.daisyos.')):core.add(entry)
for paths in ownership.values():core-=paths
ownership['daisyos-core']=core
for name, paths in ownership.items():
    with tempfile.TemporaryDirectory(prefix='daisy-package-archive-') as work:
        work=pathlib.Path(work)
        size=sum(path.lstat().st_size for path in paths if path.is_file() or path.is_symlink())
        dependencies=['pacman','polkit','kwin','systemd','dbus','networkmanager'] if name=='daisyos-core' else ['daisyos-core='+args.version]
        if name=='daisyos-mail':dependencies.append('webkit2gtk-4.1')
        metadata=f'pkgname = {name}\npkgbase = daisyos\npkgver = {args.version}-1\npkgdesc = DaisyOS managed application component\nbuilddate = {int(os.environ.get("SOURCE_DATE_EPOCH", time.time()))}\nsize = {size}\narch = x86_64\nlicense = custom\n'+''.join('depend = '+dep+'\n' for dep in dependencies)
        (work/'.PKGINFO').write_text(metadata)
        archive=cache/(name+'-'+args.version+'-1-x86_64.pkg.tar.zst')
        # Use tarfile for explicit archive paths; bsdtar applies -C to -T lists
        # differently from ordinary operands on some libarchive versions.
        intermediate=work/'payload.tar'
        def root_metadata(entry):
            entry.uid=entry.gid=0;entry.uname=entry.gname='root'
            if 'SOURCE_DATE_EPOCH' in os.environ:entry.mtime=int(os.environ['SOURCE_DATE_EPOCH'])
            return entry
        with tarfile.open(intermediate,'w',dereference=False) as tar:
            tar.add(work/'.PKGINFO',arcname='.PKGINFO',recursive=False,filter=root_metadata)
            for path in sorted(paths):tar.add(path,arcname=str(path.relative_to(root)),recursive=False,filter=root_metadata)
        subprocess.run(['bsdtar','--zstd','-cf',str(archive),'@'+str(intermediate)],check=True)
        print('Staged managed package:',name)
