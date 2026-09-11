namespace VEngine.Engine.Graphics.GL;

/// <summary>
/// Embedded GLSL 330 core shader sources for the engine's built-in rendering.
/// </summary>
internal static class DefaultShaders
{
    // ── Sprite (textured quad) ──────────────────────────────────────

    public const int MaxTextureSlots = 8;

    public const string SpriteVertex = @"#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aTexCoord;
layout(location = 2) in vec4 aColor;
layout(location = 3) in float aTexIndex;

uniform mat4 uProjection;

out vec2 vTexCoord;
out vec4 vColor;
flat out int vTexIndex;

void main()
{
    gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
    vTexCoord = aTexCoord;
    vColor = aColor;
    vTexIndex = int(aTexIndex);
}
";

    public const string SpriteFragment = @"#version 330 core
in vec2 vTexCoord;
in vec4 vColor;
flat in int vTexIndex;

uniform sampler2D uTextures[8];
uniform float uEffect; // 0 = normal, 1 = solid color with texture alpha (flash/silhouette/outline)
uniform float uClipX;  // window-space X clip line (-1 = disabled)
uniform float uClipDir; // -1 = show left of clipX, 1 = show right

out vec4 FragColor;

void main()
{
    vec4 texColor;
    switch (vTexIndex)
    {
        case 0: texColor = texture(uTextures[0], vTexCoord); break;
        case 1: texColor = texture(uTextures[1], vTexCoord); break;
        case 2: texColor = texture(uTextures[2], vTexCoord); break;
        case 3: texColor = texture(uTextures[3], vTexCoord); break;
        case 4: texColor = texture(uTextures[4], vTexCoord); break;
        case 5: texColor = texture(uTextures[5], vTexCoord); break;
        case 6: texColor = texture(uTextures[6], vTexCoord); break;
        case 7: texColor = texture(uTextures[7], vTexCoord); break;
        default: texColor = texture(uTextures[0], vTexCoord); break;
    }
    // Clip mask (portal emergence)
    if (uClipX >= 0.0)
    {
        if (uClipDir > 0.0 && gl_FragCoord.x < uClipX) discard;
        if (uClipDir < 0.0 && gl_FragCoord.x > uClipX) discard;
    }

    if (uEffect > 0.5)
        FragColor = vec4(vColor.rgb, texColor.a * vColor.a);
    else
        FragColor = texColor * vColor;
}
";

    // ── Primitive (untextured, vertex-colored) ──────────────────────

    public const string PrimitiveVertex = @"#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec4 aColor;

uniform mat4 uProjection;

out vec4 vColor;

void main()
{
    gl_Position = uProjection * vec4(aPos, 0.0, 1.0);
    vColor = aColor;
}
";

    public const string PrimitiveFragment = @"#version 330 core
in vec4 vColor;

out vec4 FragColor;

void main()
{
    FragColor = vColor;
}
";

    // ── CRT post-process (fullscreen quad) ──────────────────────────

    public const string PostProcessVertex = @"#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aTexCoord;

out vec2 vTexCoord;

void main()
{
    gl_Position = vec4(aPos, 0.0, 1.0);
    vTexCoord = aTexCoord;
}
";

    public const string PostProcessFragment = @"#version 330 core
in vec2 vTexCoord;

uniform sampler2D uTexture;
uniform vec2 uResolution;
uniform float uTime;
uniform float uChromaIntensity; // pixels of RGB offset (0 = off)
uniform float uScanlineAlpha;   // 0-1 darkness of scanlines (0 = off)
uniform float uVignetteStrength; // 0-1 edge darkening (0 = off)

out vec4 FragColor;

void main()
{
    vec2 uv = vTexCoord;
    vec4 color;

    // Chromatic aberration — sample R, G, B at offset UVs
    if (uChromaIntensity > 0.0)
    {
        vec2 offset = vec2(uChromaIntensity / uResolution.x, 0.0);
        float r = texture(uTexture, uv - offset).r;
        float g = texture(uTexture, uv).g;
        float b = texture(uTexture, uv + offset).b;
        color = vec4(r, g, b, 1.0);
    }
    else
    {
        color = texture(uTexture, uv);
    }

    // Scanlines — darken every Nth pixel row
    if (uScanlineAlpha > 0.0)
    {
        float scanline = mod(gl_FragCoord.y, 3.0) < 1.0 ? (1.0 - uScanlineAlpha) : 1.0;
        color.rgb *= scanline;
    }

    // Vignette — darken edges
    if (uVignetteStrength > 0.0)
    {
        vec2 center = uv - 0.5;
        float dist = length(center) * 1.414; // normalize so corners = 1
        float vig = smoothstep(0.3, 1.0, dist) * uVignetteStrength;
        color.rgb *= (1.0 - vig);
    }

    FragColor = color;
}
";

    // ── Video Glitch (Shadertoy-style noise + scanline + channel shift) ──

    public const string VideoGlitchFragment = @"#version 330 core
in vec2 vTexCoord;

uniform sampler2D uTexture;
uniform vec2 uResolution;
uniform float uTime;
uniform float uIntensity; // 0-1 glitch strength

out vec4 FragColor;

// Simplex noise (Ashima Arts / Ian McEwan, MIT license)
vec3 mod289(vec3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec2 mod289(vec2 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec3 permute(vec3 x) { return mod289(((x * 34.0) + 1.0) * x); }

float snoise(vec2 v) {
    const vec4 C = vec4(0.211324865405187, 0.366025403784439,
                       -0.577350269189626, 0.024390243902439);
    vec2 i = floor(v + dot(v, C.yy));
    vec2 x0 = v - i + dot(i, C.xx);
    vec2 i1 = (x0.x > x0.y) ? vec2(1.0, 0.0) : vec2(0.0, 1.0);
    vec4 x12 = x0.xyxy + C.xxzz;
    x12.xy -= i1;
    i = mod289(i);
    vec3 p = permute(permute(i.y + vec3(0.0, i1.y, 1.0)) + i.x + vec3(0.0, i1.x, 1.0));
    vec3 m = max(0.5 - vec3(dot(x0, x0), dot(x12.xy, x12.xy), dot(x12.zw, x12.zw)), 0.0);
    m = m * m; m = m * m;
    vec3 x = 2.0 * fract(p * C.www) - 1.0;
    vec3 h = abs(x) - 0.5;
    vec3 ox = floor(x + 0.5);
    vec3 a0 = x - ox;
    m *= 1.79284291400159 - 0.85373472095314 * (a0 * a0 + h * h);
    vec3 g;
    g.x = a0.x * x0.x + h.x * x0.y;
    g.yz = a0.yz * x12.xz + h.yz * x12.yw;
    return 130.0 * dot(m, g);
}

float rand(vec2 co) {
    return fract(sin(dot(co.xy, vec2(12.9898, 78.233))) * 43758.5453);
}

void main()
{
    vec2 uv = vTexCoord;
    float time = uTime * 2.0;
    float strength = clamp(uIntensity, 0.0, 1.0);

    // Large incidental noise waves
    float noise = max(0.0, snoise(vec2(time, uv.y * 0.3)) - 0.3) * (1.0 / 0.7);
    noise = noise + (snoise(vec2(time * 10.0, uv.y * 2.4)) - 0.5) * 0.15;
    noise *= strength;

    // Horizontal displacement
    float xpos = uv.x - noise * noise * 0.25;
    FragColor = texture(uTexture, vec2(xpos, uv.y));

    // Random interference lines
    FragColor.rgb = mix(FragColor.rgb, vec3(rand(vec2(uv.y * time))), noise * 0.3).rgb;

    // Scanline pattern every 4 pixels
    if (floor(mod(gl_FragCoord.y * 0.25, 2.0)) == 0.0)
        FragColor.rgb *= 1.0 - (0.15 * noise);

    // Channel shift (green/blue offset)
    FragColor.g = mix(FragColor.r, texture(uTexture, vec2(xpos + noise * 0.05, uv.y)).g, 0.25);
    FragColor.b = mix(FragColor.r, texture(uTexture, vec2(xpos - noise * 0.05, uv.y)).b, 0.25);
}
";

    // ── Coldberg TV (full CRT sim: curvature, scan shift, frame roll, color drift, noise) ──

    public const string ColdbergTVFragment = @"#version 330 core
in vec2 vTexCoord;

uniform sampler2D uTexture;
uniform vec2 uResolution;
uniform float uTime;
uniform float uIntensity;

out vec4 FragColor;

#define M_PI 3.1415926535897932384626433832795

float hash(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * vec3(443.897, 441.423, 437.195));
    p3 += dot(p3, p3.yzx + 19.19);
    return fract((p3.x + p3.y) * p3.z);
}

float procNoise(vec2 p) {
    vec2 i = floor(p); vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
               mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

float q2DNoise(vec2 gPos) {
    return procNoise(vec2(gPos.x + uTime * 9.66, gPos.y + uTime * 7.77) * 256.0);
}

float q1DNoise(float idx, float s) {
    return procNoise(vec2(idx * 256.0, uTime * s * 256.0));
}

float qScanLine(vec2 uv, float n) { return abs(sin(uv.y * M_PI * n)); }

float qVignete(vec2 uv, float q, float o) {
    float x = clamp(1.0 - distance(uv, vec2(0.5)) * q, 0.0, 1.0);
    return (log((o - 1.0 / exp(o)) * x + 1.0 / exp(o)) + o) / (log(o) + o);
}

vec2 vCrtCurvature(vec2 uv, float q) {
    return uv + (vec2(0.5) - uv) * (1.0 - distance(uv, vec2(0.5))) * q;
}

vec2 vScanShift(vec2 uv, float q, float dy, float dt) {
    return vec2(uv.x + q1DNoise(uv.y * dy, dt) * q, uv.y);
}

vec2 vFrameShift(vec2 uv, float q, float dt) {
    float s = (q1DNoise(0.5, dt) - 0.5) / 500.0;
    return vec2(uv.x, mod(uv.y + uTime * (q + s), 1.0));
}

vec2 vDirShift(vec2 uv, float angle, float q) {
    float a = (angle / 180.0) * M_PI;
    return uv + vec2(sin(a), cos(a)) * q;
}

vec4 vRGBShift(vec2 uv, float angle, float q) {
    return vec4(
        texture(uTexture, vDirShift(uv, angle, q)).r,
        texture(uTexture, uv).g,
        texture(uTexture, vDirShift(uv, -angle, q)).b, 1.0);
}

vec4 vPowerNoise(vec4 col, vec2 uv, float b, float dt, float w) {
    float s = q1DNoise(0.0, 0.001) / 500.0;
    float y = mod(uTime * (dt + s), 1.0);
    float d = 1.0 - clamp(abs(uv.y - y), 0.0, w) / w;
    return pow(col, vec4(1.0 / (1.0 + b * d)));
}

vec3 rgb2hsv(vec3 c) {
    vec4 K = vec4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
    vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
    vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y); float e = 1.0e-10;
    return vec3(abs(q.z + (q.w - q.y) / (6.0*d+e)), d/(q.x+e), q.x);
}

vec3 hsv2rgb(vec3 c) {
    vec4 K = vec4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
    vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

void main()
{
    vec2 gPos = vTexCoord;
    vec2 cPos = gPos;
    vec2 bPos;
    float str = clamp(uIntensity, 0.0, 1.0);
    float qN = q1DNoise(0.01, 0.01);

    cPos = vScanShift(cPos, 0.02 * str, 0.1, 0.1);
    cPos = vCrtCurvature(cPos, 0.3 * str);
    bPos = vCrtCurvature(gPos, 0.3 * str);
    cPos = vFrameShift(cPos, 0.01 * str, 0.001);

    vec4 cCol = vRGBShift(cPos, 100.0, 0.01 * str);

    // Color drift
    vec3 hsv = rgb2hsv(cCol.rgb);
    hsv.y = mod(hsv.y * (1.0 - qN * str), 1.0);
    cCol = vec4(hsv2rgb(hsv), 1.0);

    // Signal noise
    cCol = cCol * (1.0 - qN * 0.8 * str) + qN * 0.8 * str * q2DNoise(gPos);

    // Power line noise
    cCol = vPowerNoise(cCol, bPos, 4.0 * str, -0.2, 0.1);

    // Gamma tint
    vec4 tinted = pow(cCol, 1.0 / vec4(0.9, 0.7, 1.2, 1.0));
    cCol = mix(cCol, tinted, str);

    // Scanlines + vignette
    cCol *= mix(1.0, qScanLine(gPos, 120.0), str);
    cCol *= mix(1.0, qVignete(gPos, 1.5, 3.0), str);

    // Blend with clean based on intensity
    FragColor = mix(texture(uTexture, vTexCoord), cCol, str);
}
";
}
