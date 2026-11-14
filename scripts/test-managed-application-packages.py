#!/usr/bin/env python3
"""Exercise ISO application ownership archives using a tiny disposable payload."""
import pathlib, subprocess, tempfile
repo=pathlib.Path(__file__).resolve().parent.parent
with tempfile.TemporaryDirectory(prefix='daisy-ownership-test-') as directory:
    root=pathlib.Path(directory);payload=root/'opt/DaisyOS/shell'
    payload.mkdir(parents=True);(payload/'daisyos-shell').write_text('shell fixture')
    for app in ('Files','Mail','Notes','Calculator','AppStore','DiskManager','UIKit'):
        binary=payload/'apps'/app/('DaisyOS.'+app);binary.parent.mkdir(parents=True);binary.write_text('app fixture')
        entry=root/'usr/share/applications'/('org.daisyos.'+app.lower()+'.desktop');entry.parent.mkdir(parents=True,exist_ok=True);entry.write_text('[Desktop Entry]\nName='+app+'\n')
    subprocess.run(['python3',str(repo/'scripts/stage-managed-applications.py'),'--root',str(root)],check=True)
    archives=list((root/'var/cache/daisyos/bootstrap-packages').glob('*.pkg.tar.zst'))
    assert len(archives)==8
    owners={}
    for archive in archives:
        metadata=subprocess.check_output(['bsdtar','-xOf',str(archive),'.PKGINFO'],text=True)
        assert 'pkgname = daisyos-' in metadata
        names=subprocess.check_output(['bsdtar','-tf',str(archive)],text=True).splitlines()
        for name in names:
            if name=='.PKGINFO' or name.endswith('/'):continue
            assert name not in owners,(name,owners.get(name),archive.name)
            owners[name]=archive.name
    assert owners['opt/DaisyOS/shell/apps/Notes/DaisyOS.Notes'].startswith('daisyos-notes-')
    assert owners['opt/DaisyOS/shell/daisyos-shell'].startswith('daisyos-core-')
    assert owners['usr/lib/daisyos/register-application-packages'].startswith('daisyos-core-')
    assert owners['usr/lib/systemd/system/daisyos-register-applications.service'].startswith('daisyos-core-')
    print('PASS: eight valid bootstrap archives, disjoint file ownership, optional app payload separation, owned registration service')
