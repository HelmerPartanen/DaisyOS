#!/usr/bin/env python3
"""Check complete language catalogs, format arguments, and XAML resource references."""
from pathlib import Path
import re
import hashlib
import json
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
DIRECTORY = ROOT / "src/DaisyOS.Shell/Resources"


def entries(path):
    result = {}
    for item in ET.parse(path).getroot().findall("data"):
        key = item.attrib["name"]
        if key in result:
            raise ValueError(f"Duplicate resource: {path.name}: {key}")
        result[key] = item.findtext("value", "")
    return result


def main():
    neutral = entries(DIRECTORY / "Messages.resx")
    for path in DIRECTORY.glob("Messages.*.resx"):
        translated = entries(path)
        if neutral.keys() != translated.keys():
            raise ValueError(f"Incomplete catalog {path.name}: missing {neutral.keys() - translated.keys()}, extra {translated.keys() - neutral.keys()}")
        for key, source in neutral.items():
            # Keep argument types, alignment and formatting unchanged. Allow reordering.
            arguments = lambda text: sorted(re.findall(r'(?<!\{)\{\d+(?:,[^{}:]+)?(?::[^{}]+)?\}(?!\})', text))
            if arguments(source) != arguments(translated[key]):
                raise ValueError(f"Format arguments differ in {path.name}: {source!r}")
            if not translated[key]:
                raise ValueError(f"Empty translation in {path.name}: {key}")
    ui_attributes = {"Text", "Content", "Header", "Watermark", "ToolTip.Tip", "Title", "PlaceholderText",
                     "OnContent", "OffContent", "ToolTipText", "DetailsToolTipText"}
    # Product names and mathematical notation are intentionally language-independent.
    invariant_labels = {"LocalSend", "π"}
    for path in (ROOT / "src/DaisyOS.Shell").rglob("*.axaml"):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        for element in ET.parse(path).iter():
            for name, value in element.attrib.items():
                if name in ui_attributes and value and not value.startswith("{") and value not in invariant_labels and any(c.isalpha() for c in value):
                    raise ValueError(f"Unlocalized XAML label: {path}: {name}={value!r}")
        for key in re.findall(r'\{DynamicResource (Loc_[A-F0-9]+)\}', path.read_text()):
            if key not in neutral:
                raise ValueError(f"Missing XAML resource: {path}: {key}")
    for path in (ROOT / "src/DaisyOS.Shell").rglob("*.cs"):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        text = path.read_text(encoding="utf-8-sig")
        literals = re.findall(r'Loc\.(?:Get|Format)\("((?:[^"\\]|\\.)*)"', text)
        for pair in re.findall(r'Loc\.Get\([^"\n]*\?\s*"((?:[^"\\]|\\.)*)"\s*:\s*"((?:[^"\\]|\\.)*)"\s*\)', text):
            literals.extend(pair)
        for literal in literals:
            source = json.loads('"' + literal + '"')
            key = "Loc_" + hashlib.sha256(source.encode()).hexdigest().upper()[:12]
            if key not in neutral:
                raise ValueError(f"Missing C# resource: {path}: {source!r}")
    print(f"Validated {len(neutral)} messages, satellite coverage, format arguments and XAML references.")


if __name__ == "__main__":
    main()
