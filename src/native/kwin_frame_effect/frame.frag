#version 140
// SPDX-License-Identifier: GPL-2.0-or-later
#include "colormanagement.glsl"
#include "saturation.glsl"
in vec2 texcoord0;
out vec4 fragColor;
uniform sampler2D sampler;
uniform vec4 modulation;
uniform vec2 frameSize;
uniform vec2 textureSize;
uniform vec2 frameOffset;
uniform float radius;
uniform vec2 widths;
// KWin's QColor uniforms are already premultiplied.
uniform vec4 outerColor;
uniform vec4 innerColor;

float coverage(vec2 p, float inset, vec2 pixelDx, vec2 pixelDy) {
    vec2 halfSize = max(frameSize * 0.5 - vec2(inset), vec2(0.0));
    float r = min(max(radius - inset, 0.0), min(halfSize.x, halfSize.y));
    vec2 relative = p - frameSize * 0.5;
    vec2 q = abs(relative) - halfSize + vec2(r);
    vec2 outside = max(q, vec2(0.0));
    float len = length(outside);
    float d = len + min(max(q.x, q.y), 0.0) - r;
    vec2 normal = len > 0.0001 ? outside / len
        : (q.x > q.y ? vec2(1.0, 0.0) : vec2(0.0, 1.0));
    normal *= sign(relative);

    // Integrate the edge's tangent over the actual screen-pixel footprint.
    // A linear fwidth ramp over-blurs diagonals and makes the two thin bands
    // change weight as they turn the corner. The box filter keeps their
    // coverage consistent with the straight edges, including fractional DPI.
    float dx = abs(dot(normal, pixelDx));
    float dy = abs(dot(normal, pixelDy));
    float major = max(max(dx, dy), 0.0001);
    float minor = min(dx, dy);
    float extent = 0.5 * (major + minor);
    if (d >= extent) return 0.0;
    if (d <= -extent) return 1.0;
    if (minor < 0.0001) return clamp(0.5 - d / major, 0.0, 1.0);
    float x = extent - d;
    float u = max(x - major, 0.0);
    float v = max(x - minor, 0.0);
    return clamp((x * x - u * u - v * v) / (2.0 * major * minor), 0.0, 1.0);
}
void main() {
    vec4 source = texture(sampler, texcoord0);
    // KWin 6.8 offscreen swapchain coordinates run from top to bottom.
    vec2 p = texcoord0 * textureSize - frameOffset;
    vec2 pixelDx = dFdx(p);
    vec2 pixelDy = dFdy(p);
    vec4 color = source;
    // Preserve native shadows outside the rectangular frame. Inside it, clip
    // client pixels and paint disjoint bands; neither band can cover the other.
    if (all(greaterThanEqual(p, vec2(0.0))) && all(lessThanEqual(p, frameSize))) {
        float a = coverage(p, 0.0, pixelDx, pixelDy);
        float b = coverage(p, widths.x, pixelDx, pixelDy);
        float c = coverage(p, widths.x + widths.y, pixelDx, pixelDy);
        vec4 border = outerColor * max(a - b, 0.0) + innerColor * max(b - c, 0.0);
        color = border + source * max(a - border.a, 0.0);
    }
    color = sourceEncodingToNitsInDestinationColorspace(color);
    color = adjustSaturation(color) * modulation;
    fragColor = nitsToDestinationEncoding(color);
}
