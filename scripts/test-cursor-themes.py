#!/usr/bin/env python3
import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest

SCRIPT = Path(__file__).with_name('build-cursor-themes.py')
spec = importlib.util.spec_from_file_location('cursor_builder', SCRIPT)
builder = importlib.util.module_from_spec(spec); spec.loader.exec_module(builder)
ASSETS = builder.ROOT / 'src/DaisyOS.Shell/Assets/Cursor'

def load(path):
    data = path.read_bytes()
    magic, header, version, count = struct.unpack_from('<4I', data)
    assert magic == 0x72756358 and header == 16 and version == 0x10000
    images = []
    for i in range(count):
        kind, size, position = struct.unpack_from('<3I', data, header + i*12)
        values = struct.unpack_from('<9I', data, position)
        assert kind == 0xfffd0002 and values[:4] == (36, kind, size, 1)
        _, _, _, _, width, height, x, y, delay = values
        pixels = struct.unpack_from('<' + 'I'*(width*height), data, position+36)
        images.append((size, width, height, x, y, delay, pixels))
    return images

class CursorTests(unittest.TestCase):
    def test_real_themes_have_correct_sizes_hotspots_transparency_and_animation(self):
        with tempfile.TemporaryDirectory() as tmp:
            output = Path(tmp); builder.build(output)
            for variant in ('Dark', 'Light'):
                for style in ('Regular', 'Small'):
                    theme = output / ('DaisyOS-' + variant + ('-Small' if style == 'Small' else ''))
                    for shape, (filename, aliases) in builder.SHAPES.items():
                        source = next(p for p in (ASSETS / variant / style).iterdir() if p.name.casefold() == filename.casefold())
                        source_frames = builder.ani(source.read_bytes()) if source.suffix == '.ani' else [(builder.cur(source.read_bytes()), 0)]
                        images = load(theme / 'cursors' / shape)
                        self.assertEqual(set(builder.SIZES), {i[0] for i in images})
                        for size in builder.SIZES:
                            selected = [i for i in images if i[0] == size]
                            self.assertEqual(len(source_frames), len(selected))
                            self.assertEqual([f[1] for f in source_frames], [i[5] for i in selected])
                            for result, (frame, _) in zip(selected, source_frames):
                                image, x, y = min(frame, key=lambda item: (item[0].width()<size, abs(item[0].width()-size)))
                                self.assertEqual((size,size,min(size-1,round(x*size/image.width())),min(size-1,round(y*size/image.height()))), result[1:5])
                                self.assertTrue(any(pixel == 0 for pixel in result[6]), 'Transparent margin was lost')
                                for pixel in result[6]:
                                    alpha = pixel >> 24
                                    self.assertTrue(all(channel <= alpha for channel in ((pixel>>16)&255,(pixel>>8)&255,pixel&255)), 'Pixels are not premultiplied ARGB')
                            if shape in ('progress','wait'):
                                self.assertGreater(len(set(i[6] for i in selected)), 1)
                                self.assertEqual(2000, sum(i[5] for i in selected))
                        for alias in aliases:
                            self.assertEqual(shape, (theme/'cursors'/alias).readlink().name)
    def test_ani_sequence_and_fractional_jiffy_rates(self):
        def chunk(name, body): return name + struct.pack('<I',len(body)) + body + (b'\0' if len(body)&1 else b'')
        a=(ASSETS/'Dark/Regular/Pointer.cur').read_bytes()
        b=(ASSETS/'Dark/Regular/help.cur').read_bytes()
        contents=chunk(b'anih',struct.pack('<9I',36,2,3,0,0,0,0,3,3))
        contents+=chunk(b'seq ',struct.pack('<3I',1,0,1))+chunk(b'rate',struct.pack('<3I',1,2,3))
        contents+=chunk(b'LIST',b'fram'+chunk(b'icon',a)+chunk(b'icon',b))
        frames=builder.ani(b'RIFF'+struct.pack('<I',len(contents)+4)+b'ACON'+contents)
        self.assertEqual([17,33,50],[frame[1] for frame in frames])
        self.assertEqual(builder.cur(b)[0][0],frames[0][0][0][0])
        self.assertEqual(builder.cur(a)[0][0],frames[1][0][0][0])
    def test_malformed_assets_are_rejected(self):
        with self.assertRaises(ValueError): builder.ani(b'RIFF'+b'\0'*8)
        data=bytearray((ASSETS/'Dark/Regular/Pointer.cur').read_bytes())
        struct.pack_into('<H',data,10,999)
        with self.assertRaises(ValueError): builder.cur(data)

if __name__ == '__main__': unittest.main()
