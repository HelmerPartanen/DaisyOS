#!/usr/bin/env python3
"""Live KWin check: each transition must be active mid-flight and settle.

Requires PySide6, qdbus6 and a running KWin Wayland session with the effect loaded.
Controls only the disposable test window.
"""
import os
import subprocess,tempfile
os.environ.setdefault("QT_QPA_PLATFORM", "wayland")
from pathlib import Path
from PySide6.QtWidgets import QApplication,QWidget
from PySide6.QtCore import QTimer
app=QApplication([]);app.setQuitOnLastWindowClosed(False)
w=QWidget();w.setWindowTitle('DaisyOS Motion Frame Check');w.resize(640,420)
results=[]; root=tempfile.TemporaryDirectory(prefix='daisy-motion-');counter=0

def dbus(*args):
 return subprocess.run(['qdbus6','org.kde.KWin',*args],capture_output=True,text=True,timeout=3)
def probe(name, expected=True):
 r=dbus('/Effects','org.freedesktop.DBus.Properties.Get','org.kde.kwin.Effects','activeEffects')
 ok='daisyos-window-motion' in r.stdout;results.append((name,ok == expected)); print(name, 'PASS' if ok == expected else 'FAIL',flush=True)
def command(js):
 global counter
 counter+=1; p=Path(root.name)/f'{counter}.js';p.write_text('var w = workspace.windowList().find(w => w.caption === "DaisyOS Motion Frame Check"); if (w) { '+js+' }')
 name=f'daisy-motion-test-{counter}'
 r=dbus('/Scripting','org.kde.kwin.Scripting.loadScript',str(p),name)
 assert r.returncode==0,r.stderr
 dbus('/Scripting','org.kde.kwin.Scripting.start')
 QTimer.singleShot(300,lambda:dbus('/Scripting','org.kde.kwin.Scripting.unloadScript',name))
actions=[('open',w.show),('minimize',lambda:command('w.minimized = true;')),
 ('restore',lambda:command('w.minimized = false; workspace.activeWindow = w;')),
 ('maximize',lambda:command('w.setMaximize(true,true);')),
 ('unmaximize',lambda:command('w.setMaximize(false,false);')),
 ('hide',w.hide),('unhide',w.show),('close',w.close)]
def run_action(index=0):
 if index == len(actions):
  app.quit(); return
 name, fn = actions[index]
 fn()
 QTimer.singleShot(40, lambda:probe(name))
 def settled():
  probe(name + " settled", False)
  QTimer.singleShot(200, lambda:run_action(index + 1))
 QTimer.singleShot(450, settled)
QTimer.singleShot(100,run_action)
QTimer.singleShot(30000,app.quit)
app.exec();root.cleanup()
assert len(results) == 2 * len(actions), "runtime test timed out"
assert all(ok for _,ok in results),results
