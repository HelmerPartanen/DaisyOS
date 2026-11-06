#!/usr/bin/env python3
"""Compare the Rust port against the actual existing C# calculator state machine."""
import json,random,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
subprocess.run(['dotnet','build',str(ROOT/'scripts/calculator-parity/CalculatorParity.csproj'),'-c','Release','--nologo','-v','quiet'],check=True)
rng=random.Random(8601)
cases=[list('12')+['+']+list('34')+['='],list('200')+['+']+list('10')+['%','='],['1','/','0','='],['9','sqrt'],['0','ln'],['1','sign','sqrt'],list('171')+['fact'],['5','MS','AC','MR'],['mode','angle','angle','3','sin'],['0','inv','cos'],['2','hyp','inv','sin'],['0','.','0','0','0','0','0','0','0','1','recip'],list('9999999999')+['*','9','=']]
keys=list('0123456789')+['.','+','-','*','/','=','AC','CE','backspace','sign','%','sin','cos','tan','sqrt','sqr','log','ln','recip','fact','pi','mode','inv','hyp','angle','MC','MR','M+','M-','MS']
for _ in range(1200):cases.append([rng.choice(keys)for _ in range(rng.randint(1,75))])
reference=subprocess.run(['dotnet',str(ROOT/'scripts/calculator-parity/bin/Release/net10.0/CalculatorParity.dll')],input='\n'.join(json.dumps(c)for c in cases)+'\n',text=True,capture_output=True,check=True)
rows=reference.stdout.splitlines()
assert len(rows)==len(cases)
for index,(case,line) in enumerate(zip(cases,rows)):
    expected=json.loads(line)
    actual=json.loads(subprocess.check_output([str(ROOT/'target/debug/daisy-calculator'),'--evaluate',*case],text=True))
    assert actual==expected,(index,case,expected,actual)
print(f'PASS: {len(cases)} exact C#/Rust state comparisons (arithmetic, formatting, memory, scientific functions, errors and mode state)')
