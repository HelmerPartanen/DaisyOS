#!/usr/bin/env python3
"""Deterministic migration inventory of every first-party C#/Avalonia source."""
import argparse, hashlib, json, re
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
OUTPUT=ROOT/'docs/migration/avalonia-inventory.json'

def inventory():
    files=[]
    for path in sorted((ROOT/'src').rglob('*')):
        if path.suffix not in {'.cs','.axaml','.csproj'} or any(p in {'bin','obj','build'} for p in path.parts): continue
        text=path.read_text()
        relative=str(path.relative_to(ROOT))
        files.append({'path':relative, 'lines':len(text.splitlines()), 'sha256':hashlib.sha256(text.encode()).hexdigest(),
            'classes':re.findall(r'(?:class|interface|enum|record(?:\s+struct)?)\s+(\w+)',text),
            'xamlClass':re.findall(r'x:Class="([^"]+)"',text),
            'resources':sorted(set(re.findall(r'x:Key="([^"]+)"',text))),
            'resourceUses':sorted(set(re.findall(r'\{(?:Dynamic|Static)Resource\s+([^}\s]+)',text))),
            'handlers':sorted(set(re.findall(r'(?:Click|KeyDown|PointerPressed|SelectionChanged|TextChanged)="([^"]+)"',text))),
            'projectReferences':re.findall(r'<ProjectReference\s+Include="([^"]+)"',text),
            'packages':re.findall(r'<PackageReference\s+Include="([^"]+)"',text)})
    return {'source':'src/** excluding generated output; native and external integrations are mapped in the audit',
      'counts':{'files':len(files),'xaml':sum(f['path'].endswith('.axaml') for f in files),'csharp':sum(f['path'].endswith('.cs') for f in files),
                'lines':sum(f['lines'] for f in files)},'files':files}

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--check',action='store_true');args=p.parse_args()
    value=json.dumps(inventory(),indent=2,ensure_ascii=False)+'\n'
    if args.check:
        if not OUTPUT.exists() or OUTPUT.read_text()!=value: raise SystemExit('Avalonia inventory changed; run scripts/audit-avalonia-ui.py')
    else: OUTPUT.write_text(value)
    print(json.dumps(json.loads(value)['counts']))
