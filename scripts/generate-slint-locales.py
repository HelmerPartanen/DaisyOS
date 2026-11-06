#!/usr/bin/env python3
"""Extract the first migrated app's messages from the existing RESX catalog."""
import argparse,hashlib,json,xml.etree.ElementTree as E
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
WORDS=['Calculator','Standard','Scientific','Error','Toggle Standard / Scientific','Backspace','History']
paths=[ROOT/'src/DaisyOS.Shell/Resources/Messages.resx',ROOT/'src/DaisyOS.Shell/Resources/Messages.fi.resx']
data={}
for language,path in zip(['en-US','fi-FI'],paths):
    source={e.get('name'):e.findtext('value')for e in E.parse(path).getroot().findall('data')}
    data[language]={word:source.get('Loc_'+hashlib.sha256(word.encode()).hexdigest()[:12].upper(),word)for word in WORDS}
value=json.dumps({'sourceHashes':{str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest()for p in paths},'messages':data},indent=2,ensure_ascii=False)+'\n'
parser=argparse.ArgumentParser();parser.add_argument('--check',action='store_true');args=parser.parse_args()
target=ROOT/'crates/daisy-core/src/calculator-messages.json'
if args.check:
    if not target.exists()or target.read_text()!=value:raise SystemExit('Localization drift; regenerate Slint messages')
else:target.write_text(value)
