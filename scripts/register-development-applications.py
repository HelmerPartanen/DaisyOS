#!/usr/bin/env python3
"""Record only user-owned, separately built optional application payloads.
Source trees and user documents are never registered as uninstallable payloads.
"""
import fcntl, json, os, pathlib, sys
repo=pathlib.Path(sys.argv[1]).resolve(); configuration=sys.argv[2]
home=pathlib.Path.home();data=pathlib.Path(os.environ.get('XDG_DATA_HOME',home/'.local/share'))
registry=data/'daisyos/applications';registry.mkdir(parents=True,exist_ok=True)
with (registry/'transaction.lock').open('a') as lock:
    fcntl.flock(lock,fcntl.LOCK_EX)
    path=registry/'installations.json'
    records=json.loads(path.read_text()) if path.exists() else []
    removed=set((registry/'removed-optional-applications').read_text().lower().splitlines()) if (registry/'removed-optional-applications').exists() else set()
    for app in ('Files','Mail','Notes','Calculator','AppStore','DiskManager','UIKit'):
        if app.lower() in removed:continue
        desktop_id='org.daisyos.'+app.lower()+'.desktop'
        launcher=data/'applications'/desktop_id
        binary=repo/'src'/('DaisyOS.'+app)/'bin'/configuration/'net10.0'/('DaisyOS.'+app)
        if not binary.is_file() or not launcher.is_file():continue
        if str(binary) not in launcher.read_text():continue
        # Ownership applies only to this build's output directory.
        if binary.parent.is_symlink() or not binary.resolve().is_relative_to(repo/'src'/('DaisyOS.'+app)/'bin'):continue
        identity={'backend':4,'packageId':desktop_id,'scope':'user','container':None,'reference':None}
        records=[item for item in records if item.get('identity')!=identity]
        integrations=[str(home/'.local/bin'/('daisyos-'+app.lower()))]
        if app=='Files':integrations.append(str(data/'dbus-1/services/org.freedesktop.FileManager1.service'))
        records.append({'identity':identity,'name':app,'version':'development','sourcePath':str(binary),'desktopIds':[desktop_id],
            'ownedLaunchers':[str(launcher)],'ownedCommands':[line for line in launcher.read_text().splitlines() if line.startswith('Exec=')][:1],
            'ownedIcons':[str(icon) for icon in (data/'icons/hicolor').glob('*/apps/org.daisyos.'+app.lower()+'.*')],
            'ownedIntegrationFiles':integrations,'reviewedPaths':[str(binary.parent)]})
    temporary=path.with_suffix('.json.tmp');temporary.write_text(json.dumps(records,indent=2)+'\n');temporary.replace(path)
