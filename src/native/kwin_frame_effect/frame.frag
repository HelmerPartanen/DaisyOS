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

float distanceToFrame(vec2 p, float inset) {
    vec2 halfSize = max(frameSize * 0.5 - vec2(inset), vec2(0.0));
    float r = min(max(radius - inset, 0.0), min(halfSize.x, halfSize.y));
    vec2 q = abs(p - frameSize * 0.5) - halfSize + vec2(r);
    return length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - r;
}
float coverage(vec2 p, float inset) {
    float d = distanceToFrame(p, inset);
    float aa = max(fwidth(d), 0.0001);
    return clamp(0.5 - d / aa, 0.0, 1.0);
}
void main() {
    vec4 source = texture(sampler, texcoord0);
    // KWin 6.8 offscreen swapchain coordinates run from top to bottom.
    vec2 p = texcoord0 * textureSize - frameOffset;
    vec4 color = source;
    // Preserve native shadows outside the rectangular frame. Inside it, clip
    // client pixels and paint disjoint bands; neither band can cover the other.
    if (all(greaterThanEqual(p, vec2(0.0))) && all(lessThanEqual(p, frameSize))) {
        float a = coverage(p, 0.0);
        float b = coverage(p, widths.x);
        float c = coverage(p, widths.x + widths.y);
        vec4 border = outerColor * max(a - b, 0.0) + innerColor * max(b - c, 0.0);
        color = border + source * max(a - border.a, 0.0);
    }
    color = sourceEncodingToNitsInDestinationColorspace(color);
    color = adjustSaturation(color) * modulation;
    fragColor = nitsToDestinationEncoding(color);
}
