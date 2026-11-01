#!/usr/bin/env python3
"""Render the actual frame shader on surfaceless Mesa and inspect both bands.

Color-management helpers are identity stubs here; the isolated KWin test checks
compilation with the real KWin helpers. No window system or user session needed.
"""
import ctypes as C
from pathlib import Path

E = C.CDLL('libEGL.so.1')
G = C.CDLL('libGL.so.1')
def bind(lib, name, result, *args):
    f = getattr(lib, name)
    f.restype, f.argtypes = result, args
    return f
I, U, F, P = C.c_int, C.c_uint, C.c_float, C.c_void_p
get_display = bind(E, 'eglGetPlatformDisplay', P, U, P, P)
display = get_display(0x31DD, None, None)  # EGL_PLATFORM_SURFACELESS_MESA
assert bind(E, 'eglInitialize', U, P, P, P)(display, None, None)
assert bind(E, 'eglBindAPI', U, U)(0x30A2)  # EGL_OPENGL_API
attrs = (I * 15)(0x3033, 1, 0x3040, 8, 0x3024, 8, 0x3023, 8, 0x3022, 8, 0x3021, 8, 0x3038, 0, 0)
config, count = P(), I()
assert bind(E, 'eglChooseConfig', U, P, P, P, I, P)(display, attrs, C.byref(config), 1, C.byref(count)) and count.value
context_attrs = (I * 3)(0x3098, 3, 0x3038)
context = bind(E, 'eglCreateContext', P, P, P, P, P)(display, config, None, context_attrs)
surface_attrs = (I * 5)(0x3057, 256, 0x3056, 256, 0x3038)
surface = bind(E, 'eglCreatePbufferSurface', P, P, P, P)(display, config, surface_attrs)
assert context and surface
assert bind(E, 'eglMakeCurrent', U, P, P, P, P)(display, surface, surface, context)

def shader(kind, source):
    handle = bind(G, 'glCreateShader', U, U)(kind)
    encoded = C.c_char_p(source.encode())
    bind(G, 'glShaderSource', None, U, I, P, P)(handle, 1, C.byref(encoded), None)
    bind(G, 'glCompileShader', None, U)(handle)
    status = I()
    bind(G, 'glGetShaderiv', None, U, U, P)(handle, 0x8B81, C.byref(status))
    log = C.create_string_buffer(8192)
    bind(G, 'glGetShaderInfoLog', None, U, I, P, P)(handle, len(log), None, log)
    assert status.value, log.value.decode()
    return handle

root = Path(__file__).resolve().parents[1]
fragment = (root / 'src/native/kwin_frame_effect/frame.frag').read_text()
fragment = fragment.replace('#include "colormanagement.glsl"', '''vec4 sourceEncodingToNitsInDestinationColorspace(vec4 c) { return c; }
vec4 nitsToDestinationEncoding(vec4 c) { return c; }''').replace('#include "saturation.glsl"', 'vec4 adjustSaturation(vec4 c) { return c; }')
vertex = '''#version 140
out vec2 texcoord0;
void main() {
    vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
    texcoord0 = p;
    gl_Position = vec4(p * 2.0 - 1.0, 0, 1);
}'''
program = bind(G, 'glCreateProgram', U)()
for kind, source in [(0x8B31, vertex), (0x8B30, fragment)]:
    bind(G, 'glAttachShader', None, U, U)(program, shader(kind, source))
bind(G, 'glLinkProgram', None, U)(program)
status = I()
bind(G, 'glGetProgramiv', None, U, U, P)(program, 0x8B82, C.byref(status))
assert status.value, 'Shader link failed'
bind(G, 'glUseProgram', None, U)(program)
def uniform(name, *values):
    loc = bind(G, 'glGetUniformLocation', I, U, C.c_char_p)(program, name.encode())
    bind(G, 'glUniform' + str(len(values)) + 'f', None, I, *([F] * len(values)))(loc, *values)
tex = U()
bind(G, 'glGenTextures', None, I, P)(1, C.byref(tex))
bind(G, 'glBindTexture', None, U, U)(0x0DE1, tex)
blue = (C.c_ubyte * 4)(0, 0, 255, 255)
bind(G, 'glTexImage2D', None, U, I, I, I, I, I, U, U, P)(0x0DE1, 0, 0x8058, 1, 1, 0, 0x1908, 0x1401, blue)
for param in (0x2800, 0x2801):
    bind(G, 'glTexParameteri', None, U, U, I)(0x0DE1, param, 0x2600)
uniform('modulation', 1, 1, 1, 1)

def render(scale, radius=16, offset=0, outer=(1, 0, 0, 1), inner=(0, 1, 0, 1)):
    width, height = round(100 * scale), round(80 * scale)
    bind(G, 'glViewport', None, I, I, I, I)(0, 0, width, height)
    uniform('frameSize', width - offset * 2, height - offset * 2)
    uniform('textureSize', width, height)
    uniform('frameOffset', offset, offset)
    uniform('radius', radius * scale)
    uniform('widths', scale, scale)
    uniform('outerColor', *outer)
    uniform('innerColor', *inner)
    bind(G, 'glDrawArrays', None, U, I, I)(4, 0, 3)
    pixels = (C.c_ubyte * (width * height * 4))()
    bind(G, 'glReadPixels', None, I, I, I, I, U, U, P)(0, 0, width, height, 0x1908, 0x1401, pixels)
    def pixel(x, y):
        index = (y * width + x) * 4
        return tuple(pixels[index:index + 4])
    return width, height, pixel

for scale in (1, 1.25, 1.5, 2):
    for radius in (0, 16):
        w, h, pixel = render(scale, radius)
        for x, y in ((0, h // 2), (w - 1, h // 2), (w // 2, 0), (w // 2, h - 1)):
            assert pixel(x, y)[0] > 150, (scale, radius, x, y, pixel(x, y))
        # Find the inner band even when a fractional scale splits coverage.
        assert max(pixel(x, h // 2)[1] for x in range(1, round(3 * scale))) > 100
        assert pixel(w // 2, h // 2) == (0, 0, 255, 255)
        if radius:
            assert pixel(0, 0)[3] == 0, (scale, pixel(0, 0))
# Antialiased border pixels must retain the shape coverage, not accumulate
# opacity from the client texture under the frame bands.
w, h, reference = render(1)
expected_alpha = [reference(x, y)[3] for y in range(20) for x in range(20)]
w, h, transparent = render(1, outer=(0, 0, 0, 0), inner=(0, 0, 0, 0))
assert all(abs(a - transparent(x, y)[3]) <= 1
           for a, (x, y) in zip(expected_alpha, ((x, y) for y in range(20) for x in range(20))))
# Preserve native shadow texels outside the frame's rectangle.
w, h, pixel = render(1, offset=8)
assert pixel(0, 0) == (0, 0, 255, 255)
# Alpha is premultiplied once, and transparent borders don't blacken content.
w, h, pixel = render(1, radius=0, outer=(0.6, 0, 0, 0.6), inner=(0, 0.1, 0, 0.1))
assert all(abs(a - b) <= 1 for a, b in zip(pixel(0, h // 2), (153, 0, 102, 255)))
assert all(abs(a - b) <= 1 for a, b in zip(pixel(1, h // 2), (0, 26, 230, 255)))
print('PASS: actual GPU pixels: two bands, all edges, square/rounded corners, 100/125/150/200% scale, shadows and alpha')
