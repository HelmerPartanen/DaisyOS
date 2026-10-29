#!/usr/bin/env python3
"""Read-only comparison of portal logical geometry with KWin's output configuration."""
import json,pathlib,subprocess,sys
repo=pathlib.Path(__file__).resolve().parents[1]
helper=pathlib.Path(sys.argv[1]) if len(sys.argv)>1 else repo/'src/native/daisy_portal_capture/build/daisyos-portal-capture'
listing=subprocess.check_output([str(helper),'--list'],text=True)
actual={}
for line in listing.splitlines():
    fields=line.split('\t')
    if len(fields)==6 and fields[0]=='output':actual[fields[1]]=tuple(map(int,fields[2:]))
configuration=json.loads(subprocess.check_output(['kscreen-doctor','-j'],text=True))
checked=0
for output in configuration['outputs']:
    if not output.get('connected') or not output.get('enabled'):continue
    mode=next(m for m in output['modes'] if str(m['id'])==str(output['currentModeId']))
    w,h=mode['size']['width'],mode['size']['height']
    if output.get('rotation') in (2,8):w,h=h,w
    scale=output['scale'];expected=(output['pos']['x'],output['pos']['y'],round(w/scale),round(h/scale))
    result=actual[output['name']]
    assert result[:2]==expected[:2] and all(abs(a-b)<=1 for a,b in zip(result[2:],expected[2:])),(output['name'],scale,expected,result)
    checked+=1
    print('PASS:',output['name'],'scale',scale,'logical geometry',result)
assert checked,'No active display geometry was checked'
