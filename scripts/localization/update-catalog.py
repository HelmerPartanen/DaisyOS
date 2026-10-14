#!/usr/bin/env python3
"""Add explicit C# UI sources to the neutral .resx catalog without deleting translations."""
from pathlib import Path
import hashlib
import json
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
CATALOG = ROOT / "src/DaisyOS.Shell/Resources/Messages.resx"


def resource_key(source):
    return "Loc_" + hashlib.sha256(source.encode()).hexdigest().upper()[:12]


def main():
    tree = ET.parse(CATALOG)
    root = tree.getroot()
    entries = {entry.attrib["name"]: entry for entry in root.findall("data")}
    sources = {}
    for path in (ROOT / "src/DaisyOS.Shell").rglob("*.cs"):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        text = path.read_text(encoding="utf-8-sig")
        literals = re.findall(r'Loc\.(?:Get|Format)\("((?:[^"\\]|\\.)*)"', text)
        # Stateful labels also use conditional sources, e.g. radio on/off tips.
        for pair in re.findall(r'Loc\.Get\([^"\n]*\?\s*"((?:[^"\\]|\\.)*)"\s*:\s*"((?:[^"\\]|\\.)*)"\s*\)', text):
            literals.extend(pair)
        literals += re.findall(r'Loc\.Bind\([\s\S]*?\w+\.\w+Property, "((?:[^"\\]|\\.)*)"\)', text)
        for literal in literals:
            source = json.loads('"' + literal + '"')
            sources.setdefault(source, set()).add(str(path.relative_to(ROOT)))
        if path.name == "SettingsView.axaml.cs":
            # Search metadata keeps stable category IDs and English keywords.
            for section, title, description in re.findall(r'new\("\w+", "([^"]*)", "([^"]*)", "([^"]*)", "[^"]*"\)', text):
                for source in (section, title, description):
                    if source:
                        sources.setdefault(source, set()).add(str(path.relative_to(ROOT)))
            for source in re.findall(r'(?:Picker|Toggle)\("([^"]+)"', text) + ["Power saver", "Balanced", "Performance", "Slow", "Normal", "Fast", "Keyboard layout is provided by the active Linux session.", "Keyboard layouts are unavailable on this system."]:
                sources.setdefault(source, set()).add(str(path.relative_to(ROOT)))
            for source in re.findall(r'\("([A-Z ]+)", \[', text):
                sources.setdefault(source, set()).add(str(path.relative_to(ROOT)))
    added = 0
    for source, sites in sorted(sources.items()):
        key = resource_key(source)
        if key in entries:
            # A key remains stable when its English display copy is edited.
            original = entries[key].findtext("comment", "").partition("\n")[0].removeprefix("Source: ")
            if entries[key].findtext("value") != source and original != source:
                raise ValueError("Resource hash collision: " + key)
            continue
        entry = ET.SubElement(root, "data", {"name": key, "{http://www.w3.org/XML/1998/namespace}space": "preserve"})
        ET.SubElement(entry, "value").text = source
        ET.SubElement(entry, "comment").text = "Source: " + source + "\nUsed by: " + ", ".join(sorted(sites))
        added += 1
    ET.indent(root, space="  ")
    tree.write(CATALOG, encoding="unicode")
    print(f"Added {added} sources; retained {len(entries)} existing entries and all satellite catalogs.")


if __name__ == "__main__":
    main()
