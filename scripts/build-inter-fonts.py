#!/usr/bin/env python3
"""Generate bundled static Inter faces (requires fonttools).

Inputs: Google Fonts ofl/inter variable fonts, under Inter-OFL.txt.
Static faces avoid synthetic weights/styles in Avalonia's embedded font manager.
"""
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

root = Path(__file__).resolve().parents[1] / 'src/DaisyOS.Shell/Assets/fonts'
destination = root / 'Inter'
destination.mkdir(exist_ok=True)
for italic in (False, True):
    source = root / ('Inter-Italic-VariableFont_opsz,wght.ttf' if italic else 'Inter-VariableFont_opsz,wght.ttf')
    for weight, name in ((400, 'Regular'), (500, 'Medium'), (600, 'SemiBold'), (700, 'Bold')):
        with TTFont(source) as font:
            instance = instantiateVariableFont(font, {'wght': weight, 'opsz': 14}, inplace=False, updateFontNames=True)
            label = ('Italic' if weight == 400 else name + 'Italic') if italic else name
            instance.save(destination / f'Inter-{label}.ttf')
