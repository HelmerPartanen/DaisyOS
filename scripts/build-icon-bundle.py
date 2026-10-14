#!/usr/bin/env python3
"""Build the tree-shaken HugeIcons SVG bundle consumed by DaisyIcon."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path


ICON_REFERENCE = re.compile(
    r"<(?:[\w.-]+:)?DaisyIcon\b[^>]*?\bIcon\s*=\s*\"([a-z0-9-_]+)\"[^>]*>",
    re.IGNORECASE | re.DOTALL,
)
FILL_REFERENCE = re.compile(r"\bWeight\s*=\s*\"Fill\"", re.IGNORECASE)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--library", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--scan-root", required=True, type=Path)
    parser.add_argument("--aliases", required=True, type=Path)
    return parser.parse_args()


def manifest_icons(path: Path) -> set[tuple[str, str]]:
    icons: set[tuple[str, str]] = set()
    for line_number, raw_line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        line = raw_line.split("#", 1)[0].strip()
        if not line:
            continue
        try:
            weight, name = line.split("/", 1)
        except ValueError as error:
            raise ValueError(f"{path}:{line_number}: expected regular/icon-name or fill/icon-name") from error
        if weight not in {"regular", "fill"} or not re.fullmatch(r"[a-z0-9-_]+", name, re.IGNORECASE):
            raise ValueError(f"{path}:{line_number}: invalid icon '{line}'")
        icons.add((weight, name))
    return icons


def static_icons(root: Path) -> set[tuple[str, str]]:
    icons: set[tuple[str, str]] = set()
    for suffix in ("*.axaml", "*.cs"):
        for path in root.rglob(suffix):
            if any(part in {"bin", "obj"} for part in path.parts):
                continue
            source = path.read_text(encoding="utf-8")
            for match in ICON_REFERENCE.finditer(source):
                tag = match.group(0)
                icons.add(("fill" if FILL_REFERENCE.search(tag) else "regular", match.group(1)))
    return icons


def write_themed_svg(source: Path, destination: Path) -> None:
    """Make monochrome HugeIcons paints inherit DaisyIcon.CurrentColor.

    Preserve explicit fill=\"none\", stroke widths, caps and joins while
    replacing upstream monochrome paints with theme-aware currentColor.
    The source icon library remains unmodified.
    """
    svg = source.read_text(encoding="utf-8")
    opening_end = svg.find(">")
    if opening_end < 0 or not svg.startswith("<svg"):
        raise ValueError(f"Invalid SVG root element: {source}")

    opening = svg[:opening_end]
    if not re.search(r"\bfill\s*=", opening, re.IGNORECASE):
        svg = f'{opening} fill="currentColor"{svg[opening_end:]}'

    svg = re.sub(r'\b(stroke|fill)="(?:#[0-9a-fA-F]{3,8}|white|black)"', r'\1="currentColor"', svg)
    if not destination.exists() or destination.read_text(encoding="utf-8") != svg:
        destination.write_text(svg, encoding="utf-8")


def main() -> int:
    args = parse_args()
    library = args.library.resolve()
    output = args.output.resolve()
    manifest = args.manifest.resolve()
    scan_root = args.scan_root.resolve()
    if output == library or output == scan_root or output in library.parents or output in scan_root.parents:
        raise ValueError("Generated icon output must not contain the source library or scan root")

    if not manifest.is_file():
        raise FileNotFoundError(f"Daisy icon manifest is missing: {manifest}")
    if not library.is_dir():
        raise FileNotFoundError(
            "HugeIcons SVG development library is missing: " + str(library) +
            ". Set DaisyIconLibraryDirectory to its location.")

    aliases = json.loads(args.aliases.read_text(encoding="utf-8"))
    icons = {name.lower() for _, name in static_icons(scan_root) | manifest_icons(manifest)}
    for name, source_name in aliases.items():
        if not re.fullmatch(r"[a-z0-9-_]+", name, re.IGNORECASE) or not re.fullmatch(r"[a-z0-9-_]+", source_name, re.IGNORECASE):
            raise ValueError(f"Invalid HugeIcons alias: {name} -> {source_name}")

    def find_source(n: str) -> Path | None:
        target = aliases.get(n, n)
        for cand in (f"{target}.svg", f"{target.lower()}.svg", f"{target.capitalize()}.svg"):
            p = library / cand
            if p.is_file():
                return p
        return None

    missing = sorted(
        f"{name} -> {aliases.get(name, name)}.svg" for name in icons
        if find_source(name) is None)
    if missing:
        raise FileNotFoundError("Unknown DaisyOS HugeIcons icon(s):\n  " + "\n  ".join(missing))

    output.mkdir(parents=True, exist_ok=True)
    expected = {output / f"{name}.svg" for name in icons}
    for stale in output.rglob("*.svg"):
        if stale not in expected:
            stale.unlink()
    for name in sorted(icons):
        destination = output / f"{name}.svg"
        destination.parent.mkdir(parents=True, exist_ok=True)
        src = find_source(name)
        if src is not None:
            write_themed_svg(src, destination)

    stamp = output / ".bundle-stamp"
    stamp_contents = "\n".join(sorted(icons)) + "\n"
    stamp.write_text(stamp_contents, encoding="utf-8")
    print(f"[daisy-icons] bundled {len(icons)} HugeIcons SVGs into {output.name}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"[daisy-icons] {error}", file=sys.stderr)
        raise SystemExit(1)
