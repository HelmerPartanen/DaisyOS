#!/usr/bin/env python3
"""Convert supplied CUR/ANI assets to native Xcursor themes via libXcursor.

Uses Qt's ICO decoder (including alpha/AND masks), preserving CUR hotspots and
ANI sequence/rate chunks. PySide6 and libXcursor are build-time dependencies.
"""
import argparse
import ctypes as C
import ctypes.util
from pathlib import Path
import struct
from PySide6.QtCore import Qt
from PySide6.QtGui import QImage

ROOT = Path(__file__).resolve().parents[1]
SIZES = (24, 32, 48, 64, 96, 128)
SHAPES = {
    'default': ('Pointer.cur', ['left_ptr', 'arrow', 'top_left_arrow']),
    'text': ('beam.cur', ['xterm', 'ibeam']),
    'wait': ('busy.ani', ['watch']),
    'progress': ('working.ani', ['left_ptr_watch', 'half-busy']),
    'pointer': ('link.cur', ['hand2', 'hand1', 'pointing_hand']),
    'help': ('help.cur', ['question_arrow', 'left_ptr_help', 'whats_this']),
    'crosshair': ('precision.cur', ['cross', 'tcross']),
    'not-allowed': ('unavailable.cur', ['crossed_circle', 'forbidden', 'no-drop']),
    'ns-resize': ('vert.cur', ['size_ver', 'sb_v_double_arrow', 'n-resize', 's-resize', 'top_side', 'bottom_side', 'row-resize']),
    'ew-resize': ('horz.cur', ['size_hor', 'sb_h_double_arrow', 'w-resize', 'e-resize', 'left_side', 'right_side', 'col-resize']),
    'nwse-resize': ('dgn1.cur', ['size_fdiag', 'nw-resize', 'se-resize', 'top_left_corner', 'bottom_right_corner']),
    'nesw-resize': ('dgn2.cur', ['size_bdiag', 'ne-resize', 'sw-resize', 'top_right_corner', 'bottom_left_corner']),
    'all-scroll': ('move.cur', ['fleur', 'size_all', 'dnd-move']),
    'up-arrow': ('alternate.cur', ['up_arrow', 'sb_up_arrow', 'center_ptr']),
    'pencil': ('handwriting.cur', []),
    'person': ('person.cur', []),
    'pin': ('pin.cur', []),
}
# Widely used X11 cursor identities, including Qt/GTK compatibility hashes.
HASHES = {
    '03b6e0fcb3499374a867c041f52298f0': 'not-allowed',
    '08e8e1c95fe2fc01f976f1e063a24ccd': 'progress',
    '5c6cd98b3f3ebcb1f9c7f1c204630408': 'help',
    '9d800788f1b08800ae810202380a0822': 'pointer',
}

class Image(C.Structure):
    _fields_ = [(name, C.c_uint32) for name in ('version', 'size', 'width', 'height', 'xhot', 'yhot', 'delay')] + [('pixels', C.POINTER(C.c_uint32))]
class Images(C.Structure):
    _fields_ = [('nimage', C.c_int), ('images', C.POINTER(C.POINTER(Image))), ('name', C.c_char_p)]
lib = C.CDLL(ctypes.util.find_library('Xcursor') or 'libXcursor.so.1')
lib.XcursorImageCreate.argtypes = [C.c_int, C.c_int]; lib.XcursorImageCreate.restype = C.POINTER(Image)
lib.XcursorImagesCreate.argtypes = [C.c_int]; lib.XcursorImagesCreate.restype = C.POINTER(Images)
lib.XcursorImagesDestroy.argtypes = [C.POINTER(Images)]
lib.XcursorFilenameSaveImages.argtypes = [C.c_char_p, C.POINTER(Images)]; lib.XcursorFilenameSaveImages.restype = C.c_int

def cur(data):
    reserved, kind, count = struct.unpack_from('<HHH', data)
    if reserved or kind != 2 or not 1 <= count <= 256: raise ValueError('Invalid CUR directory')
    result = []
    for i in range(count):
        w, h, colors, _, x, y, length, offset = struct.unpack_from('<BBBBHHII', data, 6 + i * 16)
        width, height = w or 256, h or 256
        if offset + length > len(data) or x >= width or y >= height: raise ValueError('Invalid CUR image/hotspot')
        ico = struct.pack('<HHH', 0, 1, 1) + struct.pack('<BBBBHHII', w, h, colors, 0, 1, 32, length, 22) + data[offset:offset+length]
        decoded = QImage.fromData(ico, 'ICO')
        if decoded.isNull() or (decoded.width(), decoded.height()) != (width, height): raise ValueError('CUR decoding failed')
        result.append((decoded, x, y))
    return result

def chunks(data):
    position = 0
    while position < len(data):
        if position + 8 > len(data): raise ValueError('Truncated RIFF chunk')
        tag, length = struct.unpack_from('<4sI', data, position)
        end = position + 8 + length
        if end > len(data): raise ValueError('RIFF chunk outside file')
        yield tag, data[position+8:end]
        position = end + (length & 1)

def ani(data):
    if data[:4] != b'RIFF' or data[8:12] != b'ACON' or struct.unpack_from('<I', data, 4)[0]+8 != len(data): raise ValueError('Invalid ANI container')
    header = None; sequence = None; rates = None; frames = []
    for tag, body in chunks(data[12:]):
        if tag == b'anih': header = struct.unpack('<9I', body)
        elif tag == b'seq ': sequence = struct.unpack('<' + 'I'*(len(body)//4), body)
        elif tag == b'rate': rates = struct.unpack('<' + 'I'*(len(body)//4), body)
        elif tag == b'LIST' and body[:4] == b'fram':
            frames.extend(cur(frame) for tag, frame in chunks(body[4:]) if tag == b'icon')
    if header is None: raise ValueError('ANI header missing')
    _, count, steps, _, _, _, _, rate, flags = header
    if not flags & 1 or count != len(frames) or not 0 < steps <= 4096: raise ValueError('Unsupported ANI frames')
    sequence = sequence if flags & 2 else tuple(range(steps))
    if sequence is None or len(sequence) != steps or (rates is not None and len(rates) != steps): raise ValueError('Invalid ANI sequence/rates')
    result = []
    previous = 0
    elapsed = 0
    for i, index in enumerate(sequence):
        if index >= len(frames): raise ValueError('Invalid ANI frame index')
        # Jiffies are 1/60 second. Cumulative rounding preserves full loop time.
        elapsed += (rates[i] if rates is not None else rate) or 1
        end = round(elapsed * 1000 / 60)
        result.append((frames[index], end-previous)); previous = end
    return result

def save(path, source_frames):
    images = lib.XcursorImagesCreate(len(SIZES) * len(source_frames))
    if not images: raise MemoryError()
    images.contents.nimage = 0
    try:
        for size in SIZES:
            for frames, delay in source_frames:
                source, x, y = min(frames, key=lambda item: (item[0].width() < size, abs(item[0].width()-size)))
                width = source.width()
                image = source.scaled(size, size, Qt.AspectRatioMode.IgnoreAspectRatio, Qt.TransformationMode.SmoothTransformation).convertToFormat(QImage.Format.Format_ARGB32_Premultiplied)
                native = lib.XcursorImageCreate(size, size)
                if not native: raise MemoryError()
                native.contents.size = size
                native.contents.xhot = min(size-1, round(x*size/width))
                native.contents.yhot = min(size-1, round(y*size/source.height()))
                native.contents.delay = delay
                C.memmove(native.contents.pixels, bytes(image.constBits()), image.sizeInBytes())
                images.contents.images[images.contents.nimage] = native
                images.contents.nimage += 1
        if not lib.XcursorFilenameSaveImages(str(path).encode(), images): raise OSError('Xcursor save failed')
    finally: lib.XcursorImagesDestroy(images)

def build(output):
    for variant in ('Dark', 'Light'):
        for style in ('Regular', 'Small'):
            suffix = '' if style == 'Regular' else '-Small'
            theme = output / ('DaisyOS-' + variant + suffix); cursors = theme / 'cursors'
            cursors.mkdir(parents=True, exist_ok=True)
            name = variant + (' (Small)' if suffix else ' (Default)')
            (theme / 'index.theme').write_text(f'[Icon Theme]\nName={name}\nComment=Native DaisyOS cursors\nInherits=breeze_cursors\n')
            expected = set(SHAPES) | set(HASHES)
            for shape, (filename, aliases) in SHAPES.items():
                directory = ROOT / 'src/DaisyOS.Shell/Assets/Cursor' / variant / style
                source = next(file for file in directory.iterdir() if file.name.casefold() == filename.casefold())
                data = source.read_bytes()
                frames = ani(data) if filename.endswith('.ani') else [(cur(data), 0)]
                save(cursors / shape, frames)
                for alias in aliases:
                    expected.add(alias)
                    link = cursors / alias; link.unlink(missing_ok=True); link.symlink_to(shape)
            for alias, shape in HASHES.items():
                link = cursors / alias; link.unlink(missing_ok=True); link.symlink_to(shape)
            # Remove aliases generated by previous builds, never retain stale mappings.
            for file in cursors.iterdir():
                if file.name not in expected: file.unlink()
            print(f'Built {theme}: {len(SHAPES)} shapes, sizes {SIZES}, native ANI animation')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/cursors')
    build(parser.parse_args().output)
