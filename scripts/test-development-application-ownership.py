#!/usr/bin/env python3
"""Check development ownership and removal persistence without touching the session."""
import json,os,pathlib,subprocess,tempfile
script=pathlib.Path(__file__).resolve().with_name('register-development-applications.py')
for app in ('files','mail','notes','calculator','appstore','diskmanager','uikit'):
    desktop=script.parent.parent/'src/native/daisy_window_decoration/applications'/('org.daisyos.'+app+'.desktop')
    assert any(line.startswith('Exec=daisyos-'+app) for line in desktop.read_text().splitlines()),desktop
with tempfile.TemporaryDirectory(prefix='daisy-development-ownership-') as directory:
    root=pathlib.Path(directory);repo=root/'checkout';data=root/'data';home=root/'home'
    app=repo/'src/DaisyOS.Notes';output=app/'bin/Release/net10.0';output.mkdir(parents=True)
    binary=output/'DaisyOS.Notes';binary.write_text('built binary');source=app/'Source.cs';source.write_text('source stays separate')
    launcher=data/'applications/org.daisyos.notes.desktop';launcher.parent.mkdir(parents=True);launcher.write_text('[Desktop Entry]\nExec="'+str(binary)+'"\n')
    env=dict(os.environ,HOME=str(home),XDG_DATA_HOME=str(data))
    def register():subprocess.run(['python3',str(script),str(repo),'Release'],env=env,check=True)
    register();registry=data/'daisyos/applications/installations.json';records=json.loads(registry.read_text());assert len(records)==1
    assert records[0]['reviewedPaths']==[str(output)]
    assert str(source) not in json.dumps(records)
    # Removal state prevents integration from recreating ownership after a rebuild.
    registry.write_text('[]');(registry.parent/'removed-optional-applications').write_text('notes\n')
    register();assert json.loads(registry.read_text())==[];assert source.exists()
    print('PASS: development ownership restricted to app output, source preservation, removed-app rebuild persistence')
