#!/usr/bin/env python3
"""Prepare-only libalpm checks against an isolated package database; never commits."""
import json, os, pathlib, subprocess, sys, tempfile
helper=pathlib.Path(sys.argv[1]).resolve()
if os.geteuid()==0:
    raise SystemExit('Run these unprivileged tests as a regular user; root intentionally ignores test database overrides.')
with tempfile.TemporaryDirectory(prefix='daisy-alpm-tests-') as directory:
    root=pathlib.Path(directory)
    database=root/'var/lib/pacman/local';database.mkdir(parents=True)
    (database/'ALPM_DB_VERSION').write_text('9\n')
    manifest=root/'usr/share/daisyos/essential-applications.conf';manifest.parent.mkdir(parents=True);manifest.write_text('essential-session\n');manifest.chmod(0o644)
    def package(name, dependencies=(), files=()):
        path=database/(name+'-1-1');path.mkdir()
        (path/'desc').write_text('%NAME%\n'+name+'\n\n%VERSION%\n1-1\n\n%ARCH%\nx86_64\n\n%REASON%\n0\n\n%DESC%\nTest fixture\n\n'+('%DEPENDS%\n'+'\n'.join(dependencies)+'\n\n' if dependencies else ''))
        (path/'files').write_text('%FILES%\n'+'\n'.join(files)+'\n\n')
    package('essential-session',['shared-runtime'],['usr/bin/test-session'])
    package('shared-runtime')
    package('optional-app',['shared-runtime'],['usr/share/applications/optional.desktop'])
    package('dependent-app',['optional-app'],['usr/share/applications/dependent.desktop'])
    env=dict(os.environ,DAISYOS_PACKAGE_TEST_ROOT=str(root))
    def run(*arguments):
        result=subprocess.run([str(helper),*arguments],env=env,text=True,capture_output=True)
        assert result.returncode==0, result.stderr
        return json.loads(result.stdout)
    portable=root/'opt/portable-app';portable.mkdir(parents=True);(portable/'app').write_text('fixture');(portable/'app').chmod(0o755)
    launcher=root/'usr/share/applications/portable.desktop';launcher.parent.mkdir(parents=True,exist_ok=True);launcher.write_text('[Desktop Entry]\nExec=/opt/portable-app/app\n')
    portable_plan=run('plan-portable-remove',str(portable))
    assert portable_plan['desktopFiles']==['/usr/share/applications/portable.desktop'],portable_plan
    assert len(portable_plan['packages'])==1,portable_plan
    (portable/'app').write_text('changed fixture')
    assert run('plan-portable-remove',str(portable))['packages']!=portable_plan['packages']
    owned=root/'opt/owned-app';owned.mkdir();(owned/'app').write_text('owned')
    package('owned-app',files=['opt/owned-app/app'])
    blocked=subprocess.run([str(helper),'plan-portable-remove',str(owned)],env=env,text=True,capture_output=True)
    assert blocked.returncode!=0 and 'package-owned' in blocked.stderr
    before={str(p.relative_to(root)):p.read_bytes() for p in root.rglob('*') if p.is_file()}
    plan=run('plan-remove','optional-app')
    assert not plan['protected'],plan
    assert set(plan['packages'])=={'remove:optional-app=1-1','remove:dependent-app=1-1'},plan
    assert len(plan['desktopFiles'])==2,plan
    protected=run('plan-remove','shared-runtime')
    assert protected['protected'] and protected['error'],protected
    inventory=run('list')
    assert {item['identity']['packageId'] for item in inventory}=={'optional-app','dependent-app'},inventory
    attempt=subprocess.run([str(helper),'remove','optional-app','0'*64],env=env,text=True,capture_output=True)
    assert attempt.returncode!=0 and 'authenticated' in attempt.stderr
    after={str(p.relative_to(root)):p.read_bytes() for p in root.rglob('*') if p.is_file()}
    assert before==after,'Prepare-only operations modified the isolated filesystem'
    print('PASS: complete removal plans, dependency cascade, essential dependency protection, desktop ownership, commit privilege gate, read-only database preparation')
