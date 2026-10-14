#!/usr/bin/env python3
"""Create repeatable Files fixtures in an explicitly supplied scratch directory."""
import argparse
import struct
import zlib
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("root", type=Path)
parser.add_argument("--replace-images", action="store_true", help="Regenerate existing scratch PNG fixtures")
args = parser.parse_args()
# A valid patterned PNG lets the UI runner verify actual decoding, not only job scheduling.
def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff)
width, height = 64, 48
rows = b"".join(b"\0" + bytes(value for x in range(width) for value in (40 + x * 2, 70 + y * 3, 200)) for y in range(height))
image = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b"")

for label, count, extensions in [("1k", 1000, [".txt"]), ("5k", 5000, [".txt"]), ("25k", 25000, [".txt"]), ("100k", 100000, [".txt"]), ("images", 5000, [".png"]), ("mixed", 5000, [".txt", ".png", ".json", ".zip", ".mp4", ".pdf"])]:
    directory = args.root / label
    directory.mkdir(parents=True, exist_ok=True)
    for index in range(count):
        extension = extensions[index % len(extensions)]
        path = directory / f"entry-{index:06d}{extension}"
        if not path.exists() or (args.replace_images and extension == ".png"):
            path.write_bytes(image if extension == ".png" else b"benchmark\n")
    print(f"{directory}: {count} entries")
