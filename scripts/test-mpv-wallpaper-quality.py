#!/usr/bin/env python3
"""Check the GPU SDR preview against explicit zscale color conversion."""
import ctypes
import pathlib
import subprocess
import tempfile
import struct
import math

ROOT = pathlib.Path(__file__).resolve().parents[1]
LIBRARY = ROOT / "src/native/daisy_mpv_wallpaper/build/libdaisy_mpv_wallpaper.so"
HELPER = LIBRARY.parent / "daisy-mpv-preview"
lib = ctypes.CDLL(str(LIBRARY))
lib.daisy_mpv_preview.argtypes = [ctypes.c_char_p, ctypes.c_int, ctypes.c_int, ctypes.c_void_p, ctypes.c_int]
WIDTH, HEIGHT = 128, 72

def run(args):
    return subprocess.check_output(["ffmpeg", "-v", "error", *args])

def preview(path):
    pixels = ctypes.create_string_buffer(WIDTH * HEIGHT * 4)
    result = lib.daisy_mpv_preview(str(path).encode(), WIDTH, HEIGHT, pixels, WIDTH * 4)
    assert result == 0, (path, result)
    return pixels.raw

with tempfile.TemporaryDirectory(prefix="daisy-video-color-") as directory:
    for matrix, primaries in [("smpte170m", "smpte170m"), ("bt709", "bt709"), ("bt709", "smpte432")]:
        for bit_depth in [8, 10]:
            for color_range in ["tv", "pc"]:
                path = pathlib.Path(directory) / f"{matrix}-{primaries}-{bit_depth}-{color_range}.mp4"
                pixel_format = "yuv420p10le" if bit_depth == 10 else "yuv420p"
                run(["-f", "lavfi", "-i", "color=c=0x705a40:s=128x72:r=30:d=1",
                     "-vf", f"scale=in_range=tv:out_range={color_range},format={pixel_format},setparams=color_primaries={primaries}:color_trc=bt470m:colorspace={matrix}:range={color_range}",
                     "-c:v", "libx265", "-x265-params", "lossless=1:pools=none:frame-threads=2:log-level=error", "-color_primaries", primaries, "-color_trc", "bt470m",
                     "-colorspace", matrix, "-color_range", color_range, "-y", str(path)])
                linear = run(["-i", str(path), "-vf",
                              f"zscale=pin={primaries}:tin=bt470m:min={matrix}:rin={color_range}:p=709:t=linear:m=gbr:r=full,format=gbrpf32le",
                              "-frames:v", "1", "-pix_fmt", "gbrpf32le", "-f", "rawvideo", "-"])
                floats = struct.unpack("<" + "f" * (WIDTH * HEIGHT * 3), linear)
                count = WIDTH * HEIGHT
                expected = bytearray()
                for i in range(count):
                    for index in [count + i, i, 2 * count + i]:
                        value = max(0.0, min(1.0, floats[index]))
                        value = 12.92 * value if value <= 0.0031308 else 1.055 * value ** (1 / 2.4) - 0.055
                        expected.append(round(value * 255))
                    expected.append(255)
                actual = preview(path)
                helper_output = pathlib.Path(directory) / "helper poster.bgra"
                subprocess.run([str(HELPER), str(path), str(WIDTH), str(HEIGHT), str(helper_output)], check=True, timeout=20, capture_output=True)
                assert helper_output.read_bytes() == actual, "Isolated preview changed color or pixel orientation"
                differences = [abs(a - b) for i, (a, b) in enumerate(zip(actual, expected)) if i % 4 != 3]
                mean = sum(differences) / len(differences)
                maximum = max(differences)
                assert mean <= 2 and maximum <= 4, (path.name, mean, maximum)
                assert actual == preview(path), "Static source gained temporal noise or exposure changes"
                print(path.name, f"mean error={mean:.3f}, max={maximum}, repeat stable")
print("GPU preview color checks passed")
