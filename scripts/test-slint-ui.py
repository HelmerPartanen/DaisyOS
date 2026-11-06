#!/usr/bin/env python3
"""Real Wayland/KWin captures of migrated UI, isolated from user preferences.
Captures only these app clients. Protocol evidence excludes input/clipboard logs.
"""
import argparse,json,os,struct,subprocess,tempfile,time,zlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--binary-dir',type=Path,default=ROOT/'target/debug');p.add_argument('--output',type=Path,default=ROOT/'artifacts/rust-ui/acceptance');args=p.parse_args()
args.output.mkdir(parents=True,exist_ok=True)
def first_pixel(path):
    raw=Path(path).read_bytes();offset=8;data=b''
    assert raw[24:26]==bytes([8,6]), 'Expected RGBA8 PNG'
    while offset<len(raw):
        size=struct.unpack('>I',raw[offset:offset+4])[0];kind=raw[offset+4:offset+8]
        if kind==b'IDAT':data+=raw[offset+8:offset+8+size]
        offset+=12+size
    # At the first pixel every PNG predictor has zero left/up/upper-left.
    return list(zlib.decompress(data)[1:5])
rows=[]
with tempfile.TemporaryDirectory(prefix='daisy-slint-acceptance-') as temp:
    config=Path(temp)/'DaisyOS';config.mkdir()
    for dark in [False,True]:
      (config/'appearance.json').write_text(json.dumps({'themeMode':int(dark),'accentColor':8,'reduceMotion':True,'systemFontFamily':'Atkinson Hyperlegible','languageTag':'en-US','textScalePercent':100}))
      for scale in [1,1.5,2]:
        for scientific in [False,True]:
          name=f'calculator-{"dark"if dark else"light"}-{"scientific"if scientific else"standard"}-{scale}x'
          target=args.output/(name+'.png');env=os.environ.copy();env.update(XDG_CONFIG_HOME=temp,SLINT_SCALE_FACTOR=str(scale),WAYLAND_DEBUG='client')
          command=[str(args.binary_dir/'daisy-calculator'),'--require-blur','--snapshot',str(target)]
          if scientific:command.append('--scientific')
          result=subprocess.run(command,env=env,capture_output=True,text=True,timeout=20)
          if result.returncode:raise AssertionError((name,result.returncode,result.stderr[-4000:]))
          evidence=[line for line in result.stderr.splitlines()if 'ext_background_effect_'in line or '[kwin-blur]'in line]
          assert any('get_background_effect('in line for line in evidence),(name,evidence)
          assert any('set_blur_region('in line for line in evidence),(name,evidence)
          assert 'protocol error'not in result.stderr.lower(),name
          raw=target.read_bytes();assert raw[:8]==b'\x89PNG\r\n\x1a\n';width,height=struct.unpack('>II',raw[16:24])
          assert (width,height)==(round(360*scale),round(560*scale)),(name,width,height)
          rows.append({'case':name,'pixels':[width,height],'exit':result.returncode,'blurProtocol':'ext_background_effect_v1'})
          (args.output/(name+'.protocol.txt')).write_text('\n'.join(evidence)+'\n')
      env=os.environ.copy();env.update(XDG_CONFIG_HOME=temp,SLINT_SCALE_FACTOR='1');env.pop('WAYLAND_DEBUG',None)
      target=args.output/f'gallery-{"dark"if dark else"light"}.png'
      subprocess.run([str(args.binary_dir/'daisy-ui-gallery'),'--snapshot',str(target)],env=env,capture_output=True,check=True,timeout=20)
    # Observe a live settings change without recreating the Slint window.
    (config/'appearance.json').write_text(json.dumps({'themeMode':1,'languageTag':'en-US'}))
    live=args.output/'calculator-live-theme.png';env=os.environ.copy();env.update(XDG_CONFIG_HOME=temp,SLINT_SCALE_FACTOR='1');env.pop('WAYLAND_DEBUG',None)
    process=subprocess.Popen([str(args.binary_dir/'daisy-calculator'),'--snapshot',str(live),'--quit-after-ms','1600'],env=env,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
    time.sleep(.35);(config/'appearance.json').write_text(json.dumps({'themeMode':0,'languageTag':'fi-FI'}))
    _,error=process.communicate(timeout=20);assert process.returncode==0,error
    pixel=first_pixel(live);assert pixel[3]>120 and max(pixel[:3])>100,pixel
    # High contrast uses the existing opaque black background, not transparent glass.
    (config/'appearance.json').write_text(json.dumps({'themeMode':1,'highContrast':True,'accentColor':1}))
    target=args.output/'calculator-high-contrast.png'
    subprocess.run([str(args.binary_dir/'daisy-calculator'),'--snapshot',str(target)],env=env,capture_output=True,check=True,timeout=20)
    assert first_pixel(target)==[0,0,0,255],first_pixel(target)
(args.output/'results.json').write_text(json.dumps({'cases':rows,'liveAppearanceChange':True,'highContrast':True,'limitations':['Client snapshots exclude compositor pixels and decorations.','No claim of layer-shell, IME, regional-mask visual or complete app cutover parity.']},indent=2)+'\n')
print(f'PASS: {len(rows)} real Wayland calculator captures, light/dark, standard/scientific, 100/150/200% renderer scale, KWin blur requests; two galleries')
