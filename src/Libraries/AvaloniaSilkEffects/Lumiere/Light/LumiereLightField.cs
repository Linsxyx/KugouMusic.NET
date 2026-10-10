using System.Numerics;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects.Lumiere.Light;

/// <summary>Frame height units: center and size of the text region where interference fringes dim.</summary>
public readonly record struct LumiereRegion(double Cx, double Cy, double W, double H);

public sealed class LumiereLightFieldFrame
{
    public List<LumiereResolvedBeam> Beams { get; } = new(LumiereLight.MaxBeams * 2);
    public LumiereLightRig Rig { get; set; } = null!;
    public double Time { get; set; }
    /// <summary>Overall fog density multiplier (tuning, audio).</summary>
    public double FogScale { get; set; } = 1;
    /// <summary>Light color (0..1) for the fog tint and the glare.</summary>
    public Vector3 Color { get; set; } = Vector3.One;
    public double GlareScale { get; set; } = 1;
    /// <summary>Premultiplied dark field drawn under the light; Folia always passes zero.</summary>
    public Vector4 Dark { get; set; }
    public double Octaves { get; set; } = 5;
    /// <summary>Text region in frame fractions; fringes dim inside it.</summary>
    public LumiereRegion? TextRegion { get; set; }
}

/// <summary>
/// Folia lumiere/light/lightFieldShader.ts: one quad over the frame whose fragment shader sums the volumetric
/// beams × fog density (Tyndall), the fog base, caustics, interference fringes and the source glare.
/// beamMask is identical to <see cref="LumiereLight.BeamMask"/>. Output is valid premultiplied color with
/// alpha = max channel, so normal blending is c + dst·(1 − max c), close to screen without additive blending.
/// The quad lives in stage coordinates: the fog and beams move with the camera; only the dither is per pixel.
/// </summary>
internal sealed class LumiereLightField : IDisposable
{
    private const string Vertex = """
        #version 330 core
        uniform vec4 uRect;      // x0, y0, x1, y1 in stage logical pixels
        uniform mat3 uTransform; // stage -> target pixels
        uniform vec2 uViewport;
        out vec2 vPos;
        void main() {
            vec2 corner = vec2(float(gl_VertexID & 1), float(gl_VertexID >> 1));
            vec2 p = mix(uRect.xy, uRect.zw, corner);
            vec2 px = (uTransform * vec3(p, 1.0)).xy;
            gl_Position = vec4(px.x * 2.0 / uViewport.x - 1.0, 1.0 - px.y * 2.0 / uViewport.y, 0.0, 1.0);
            vPos = p;
        }
        """;

    private const string Fragment = """
        #version 330 core
        in vec2 vPos;
        out vec4 finalColor;

        #define MAX_BEAMS 6

        uniform float uAlpha;
        uniform vec4 uBeamA[MAX_BEAMS]; // ox, oy, dx, dy
        uniform vec4 uBeamB[MAX_BEAMS]; // halfWidth, tanSpread, softness, length
        uniform vec4 uBeamC[MAX_BEAMS]; // intensity, streaks, streakFreq, streakPhase
        uniform vec4 uBeamD[MAX_BEAMS]; // r, g, b, core
        uniform vec4 uBeamE[MAX_BEAMS]; // gobo: pattern, frequency, duty, phase
        uniform vec4 uBeamF[MAX_BEAMS]; // spectrum, reach, 0, 0
        uniform vec4 uFrame;            // width, height, time, beamCount
        uniform vec4 uFogA;             // density, tyndallBase, scale, warp
        uniform vec4 uFogB;             // driftX, driftY, ambient, octaves
        uniform vec3 uFogColor;
        uniform vec4 uGlare;            // x, y, radius, intensity
        uniform vec4 uGlareB;           // streak, 0, 0, 0
        uniform vec3 uGlareColor;
        uniform vec4 uDark;             // premultiplied dark field rgb, alpha
        uniform vec4 uCaustic;          // scale, speed, inBeam, enabled
        uniform vec4 uCausticFloor;     // cx, cy, rx, ry (height units)
        uniform vec4 uCausticB;         // floor strength, 0, 0, 0
        uniform vec4 uWave;             // mode, frequency, speed, strength
        uniform vec4 uWaveB;            // cx, cy, radius, separation (height units)
        uniform vec4 uShield;           // text region cx, cy, rx, ry (height units)
        uniform vec4 uShieldB;          // x: how much fringes dim in the text region

        float hash11(float x) {
            float p = fract(x * 0.1031);
            p *= p + 33.33;
            p *= p + p;
            return fract(p);
        }

        float hash12(vec2 p) {
            vec3 p3 = fract(vec3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return fract((p3.x + p3.y) * p3.z);
        }

        float valueNoise1(float x) {
            float i = floor(x);
            float f = x - i;
            float u = f * f * (3.0 - 2.0 * f);
            return mix(hash11(i), hash11(i + 1.0), u);
        }

        float valueNoise2(vec2 p) {
            vec2 i = floor(p);
            vec2 f = p - i;
            vec2 u = f * f * (3.0 - 2.0 * f);
            float a = hash12(i);
            float b = hash12(i + vec2(1.0, 0.0));
            float c = hash12(i + vec2(0.0, 1.0));
            float d = hash12(i + vec2(1.0, 1.0));
            return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
        }

        float fbm(vec2 p, float octaves) {
            float sum = 0.0;
            float amp = 0.5;
            float norm = 0.0;
            mat2 rot = mat2(0.8, -0.6, 0.6, 0.8);
            for (int i = 0; i < 6; i++) {
                if (float(i) >= octaves) break;
                sum += amp * valueNoise2(p);
                norm += amp;
                p = rot * p * 2.03 + vec2(17.1, 9.2);
                amp *= 0.5;
            }
            return sum / max(norm, 1e-4);
        }

        float fog(vec2 p, float t) {
            vec2 drift = uFogB.xy * t;
            vec2 q = (p - drift) * uFogA.z;
            float octaves = uFogB.w;
            vec2 w = vec2(
                fbm(q + vec2(0.0, t * 0.07), 3.0),
                fbm(q + vec2(5.2, 1.3) - vec2(t * 0.05, 0.0), 3.0)
            );
            float n = fbm(q + uFogA.w * (w - 0.5) * 2.0, octaves);
            return smoothstep(0.28, 0.82, n);
        }

        float band(float x, float duty) {
            float f = fract(x);
            return min(1.0, 1.0 - smoothstep(duty - 0.06, duty + 0.06, f) + smoothstep(0.94, 1.0, f));
        }

        float goboPattern(vec4 E, float u, float v) {
            float pattern = E.x;
            if (pattern < 1.5) return band(u * E.y * 0.5 + E.w, E.z);
            if (pattern < 2.5) return smoothstep(E.z * 0.5, E.z * 0.5 + 0.08, abs(u));
            if (pattern < 3.5) return band(u * E.y * 0.5 + v * E.y * 0.8 + E.w, E.z) * band(u * E.y * 0.5 - v * E.y * 0.8 - E.w, E.z);
            return smoothstep(E.z - 0.12, E.z + 0.12, valueNoise2(vec2(u * E.y, v * E.y * 0.6 + E.w)));
        }

        float goboMask(vec4 E, float u, float v) {
            if (E.x < 0.5) return 1.0;
            return 0.12 + 0.88 * goboPattern(E, u, v);
        }

        vec2 beamMask(vec4 A, vec4 B, vec4 C, float coreAmount, vec4 E, float reach, vec2 p) {
            vec2 d = p - A.xy;
            float along = dot(d, A.zw);
            if (along <= 0.0) return vec2(0.0);
            float signedPerp = d.x * -A.w + d.y * A.z;
            float half_ = abs(B.x + along * B.y) + 1e-4;
            float s = abs(signedPerp) / half_;
            float edge = smoothstep(1.0, 1.0 - max(B.z, 0.02), s);
            if (edge <= 0.0) return vec2(0.0);
            float signedS = s * sign(signedPerp);
            float streak = 1.0 - C.y + C.y * valueNoise1(signedS * C.z + C.w);
            float core = 1.0 - coreAmount + coreAmount * (1.0 - s * s);
            float falloff = exp(-along / max(B.w, 0.001));
            float reachMask = reach > 0.0 ? 1.0 - smoothstep(reach - max(0.04, half_ * 2.5), reach, along) : 1.0;
            float start = smoothstep(0.0, abs(B.x) * 2.0 + 0.01, along);
            return vec2(edge * streak * core * falloff * goboMask(E, signedS, along) * reachMask * start * C.x, signedS);
        }

        vec3 spectrumColor(float u) {
            float h = clamp(0.5 + 0.5 * u, 0.0, 1.0) * 0.8;
            vec3 k = clamp(abs(fract(vec3(h) + vec3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0, 0.0, 1.0);
            return mix(vec3(1.0), k, 0.85);
        }

        float causticField(vec2 p, float t) {
            vec2 q = p * uCaustic.x * 6.2831853 - 250.0;
            vec2 i = q;
            float c = 1.0;
            float inten = 0.005;
            for (int n = 0; n < 4; n++) {
                float tt = t * (1.0 - (3.5 / float(n + 1)));
                i = q + vec2(cos(tt - i.x) + sin(tt + i.y), sin(tt - i.y) + cos(tt + i.x));
                c += 1.0 / length(vec2(q.x / (sin(i.x + tt) / inten), q.y / (cos(i.y + tt) / inten)));
            }
            c /= 4.0;
            c = 1.17 - pow(c, 1.4);
            return clamp(pow(abs(c), 8.0), 0.0, 3.0);
        }

        float wavePattern(vec2 p, float t) {
            vec2 d = p - uWaveB.xy;
            float radius = max(uWaveB.z, 1e-3);
            float r = length(d) / radius;
            float mask = smoothstep(1.0, 0.55, r);
            float mode = uWave.x;
            float f = uWave.y;
            float phase = t * uWave.z;
            float v;
            if (mode < 1.5) {
                v = 0.5 + 0.5 * cos(r * r * f * 6.0 - phase);
            } else if (mode < 2.5) {
                float x = d.x / radius;
                float y = d.y / radius;
                float xc = x * (1.0 + 0.35 * y * y);
                v = pow(cos(xc * f * 3.0 - phase * 0.3), 2.0) * exp(-xc * xc * 2.5);
                mask = smoothstep(1.0, 0.25, length(vec2(x * 0.85, y * 1.5)));
            } else if (mode < 3.5) {
                vec2 s1 = vec2(-uWaveB.w * 0.5, 0.0);
                float d1 = length(d - s1);
                float d2 = length(d + s1);
                v = (0.5 + 0.5 * cos((d1 - d2) * f * 20.0)) * (0.5 + 0.5 * cos((d1 + d2) * f * 6.0 - phase));
            } else {
                float x = r * f * 4.0 + 1e-3;
                float sc = sin(x) / x;
                v = min(sc * sc * 4.0, 1.5);
            }
            vec2 q = (p - uShield.xy) / max(uShield.zw, vec2(1e-3));
            float shield = 1.0 - uShieldB.x * smoothstep(1.0, 0.35, length(q));
            return v * mask * shield * uWave.w;
        }

        void main(void) {
            float H = uFrame.y;
            float t = uFrame.z;
            vec2 p = vPos / H;

            float density = fog(p, t);
            float tyndall = uFogA.y + (1.0 - uFogA.y) * density * uFogA.x;

            float caustic = uCaustic.w > 0.5 ? causticField(p, t * uCaustic.y + 23.0) : 0.0;
            float causticLift = mix(1.0, 0.25 + 1.5 * caustic, uCaustic.w > 0.5 ? uCaustic.z : 0.0);

            vec3 light = vec3(0.0);
            int count = int(uFrame.w + 0.5);
            for (int i = 0; i < MAX_BEAMS; i++) {
                if (i >= count) break;
                vec4 D = uBeamD[i];
                vec2 m = beamMask(uBeamA[i], uBeamB[i], uBeamC[i], D.w, uBeamE[i], uBeamF[i].y, p);
                vec3 color = D.rgb;
                float spectrum = uBeamF[i].x;
                if (spectrum > 0.0) color = mix(color, spectrumColor(m.y) * max(max(color.r, color.g), color.b), spectrum);
                light += color * m.x;
            }
            light *= tyndall * causticLift;

            if (uCaustic.w > 0.5 && uCausticB.x > 0.0) {
                vec2 e = (p - uCausticFloor.xy) / max(uCausticFloor.zw, vec2(1e-3));
                light += uFogColor * caustic * uCausticB.x * smoothstep(1.0, 0.6, length(e));
            }
            if (uWave.x > 0.5) light += uFogColor * wavePattern(p, t);

            light += uFogColor * density * uFogB.z;

            if (uGlare.w > 0.0) {
                vec2 g = p - uGlare.xy;
                float r = max(uGlare.z, 1e-3);
                float dist = length(g);
                float glare = exp(-dist / r) * 0.55 + exp(-(dist * dist) / (r * r * 0.03));
                float streak = exp(-abs(g.y) / (r * 0.035)) * exp(-abs(g.x) / (r * 7.0)) * uGlareB.x;
                light += uGlareColor * (glare + streak) * uGlare.w;
            }

            // Hue-preserving compression on the max channel; only the very brightest whitens slightly.
            float peak = max(max(light.r, light.g), light.b);
            float mapped = 1.0 - exp(-peak);
            vec3 c = peak > 1e-5 ? light / peak * mapped : vec3(0.0);
            c = mix(c, vec3(mapped), 0.3 * mapped * mapped * mapped);
            c += (hash12(gl_FragCoord.xy) - 0.5) / 255.0;
            c = clamp(c, 0.0, 1.0);
            float a = max(max(c.r, c.g), c.b);

            vec3 rgb = c + uDark.rgb * (1.0 - a);
            float alpha = a + uDark.a * (1.0 - a);
            finalColor = vec4(rgb, alpha) * uAlpha;
        }
        """;

    private readonly GL _gl;
    private readonly EffectShaderProgram _program;
    private readonly uint _vao;
    private readonly float[] _a = new float[LumiereLight.MaxBeams * 4];
    private readonly float[] _b = new float[LumiereLight.MaxBeams * 4];
    private readonly float[] _c = new float[LumiereLight.MaxBeams * 4];
    private readonly float[] _d = new float[LumiereLight.MaxBeams * 4];
    private readonly float[] _e = new float[LumiereLight.MaxBeams * 4];
    private readonly float[] _f = new float[LumiereLight.MaxBeams * 4];

    public LumiereLightField(GL gl)
    {
        _gl = gl;
        _program = new EffectShaderProgram(gl, Vertex, Fragment, "lumiere-light-field");
        _vao = gl.GenVertexArray();
    }

    /// <param name="transform">Stage logical pixels → target pixels.</param>
    /// <param name="viewport">Target size in pixels.</param>
    /// <param name="width">Logical frame width; with <paramref name="height"/> it defines the light field units.</param>
    /// <param name="margin">Logical pixels the quad extends past each edge, so camera moves never expose a seam.</param>
    public unsafe void Render(LumiereLightFieldFrame frame, Matrix3x2 transform, Vector2 viewport,
        float width, float height, float margin, float alpha = 1)
    {
        Array.Clear(_a);
        Array.Clear(_b);
        Array.Clear(_c);
        Array.Clear(_d);
        Array.Clear(_e);
        Array.Clear(_f);
        var beams = frame.Beams;
        var count = Math.Min(beams.Count, LumiereLight.MaxBeams);
        for (var index = 0; index < count; index++)
        {
            var beam = beams[index];
            var o = index * 4;
            Set(_a, o, beam.Ox, beam.Oy, beam.Dx, beam.Dy);
            Set(_b, o, beam.HalfWidth, beam.TanSpread, beam.Softness, beam.Length);
            Set(_c, o, beam.Intensity, beam.Streaks, beam.StreakFreq, beam.StreakPhase);
            Set(_d, o, beam.R, beam.G, beam.B, beam.Core);
            Set(_e, o, beam.GoboPattern, beam.GoboFreq, beam.GoboDuty, beam.GoboPhase);
            Set(_f, o, beam.Spectrum, beam.Reach, 0, 0);
        }

        var gl = _gl;
        _program.Use();
        gl.Uniform4(_program.Uniform("uRect"), -margin, -margin, width + margin, height + margin);
        var matrix = stackalloc float[9]
        {
            transform.M11, transform.M12, 0,
            transform.M21, transform.M22, 0,
            transform.M31, transform.M32, 1,
        };
        gl.UniformMatrix3(_program.Uniform("uTransform"), 1, false, matrix);
        gl.Uniform2(_program.Uniform("uViewport"), viewport.X, viewport.Y);
        gl.Uniform1(_program.Uniform("uAlpha"), alpha);
        UploadArray("uBeamA[0]", _a);
        UploadArray("uBeamB[0]", _b);
        UploadArray("uBeamC[0]", _c);
        UploadArray("uBeamD[0]", _d);
        UploadArray("uBeamE[0]", _e);
        UploadArray("uBeamF[0]", _f);
        gl.Uniform4(_program.Uniform("uFrame"), width, height, (float)frame.Time, count);

        var rig = frame.Rig;
        var fog = rig.Fog;
        gl.Uniform4(_program.Uniform("uFogA"), (float)(fog.Density * frame.FogScale), (float)fog.TyndallBase,
            (float)fog.Scale, (float)fog.Warp);
        gl.Uniform4(_program.Uniform("uFogB"), (float)fog.DriftX, (float)fog.DriftY,
            (float)(fog.Ambient * frame.FogScale), (float)frame.Octaves);
        gl.Uniform3(_program.Uniform("uFogColor"), frame.Color.X, frame.Color.Y, frame.Color.Z);

        var aspect = (double)width / height;
        var glare = rig.Glare;
        gl.Uniform4(_program.Uniform("uGlare"),
            glare is null ? 0 : (float)(glare.X * aspect),
            glare is null ? 0 : (float)glare.Y,
            glare is null ? 0 : (float)glare.Radius,
            glare is null ? 0 : (float)(glare.Intensity * frame.GlareScale));
        gl.Uniform4(_program.Uniform("uGlareB"), glare is null ? 0 : (float)glare.Streak, 0, 0, 0);
        gl.Uniform3(_program.Uniform("uGlareColor"), frame.Color.X, frame.Color.Y, frame.Color.Z);
        gl.Uniform4(_program.Uniform("uDark"), frame.Dark.X, frame.Dark.Y, frame.Dark.Z, frame.Dark.W);

        var caustic = rig.Caustic;
        var floor = caustic?.Floor;
        gl.Uniform4(_program.Uniform("uCaustic"), (float)(caustic?.Scale ?? 0), (float)(caustic?.Speed ?? 0),
            (float)(caustic?.InBeam ?? 0), caustic is null ? 0 : 1);
        gl.Uniform4(_program.Uniform("uCausticFloor"), (float)((floor?.Cx ?? 0) * aspect), (float)(floor?.Cy ?? 0),
            (float)((floor?.Rx ?? 0) * aspect), (float)(floor?.Ry ?? 0));
        gl.Uniform4(_program.Uniform("uCausticB"), (float)((floor?.Strength ?? 0) * frame.GlareScale), 0, 0, 0);

        var wave = rig.Wave;
        gl.Uniform4(_program.Uniform("uWave"), wave is null ? 0 : (int)wave.Mode, (float)(wave?.Frequency ?? 0),
            (float)(wave?.Speed ?? 0), (float)((wave?.Strength ?? 0) * frame.GlareScale));
        gl.Uniform4(_program.Uniform("uWaveB"), (float)((wave?.Cx ?? 0) * aspect), (float)(wave?.Cy ?? 0),
            (float)(wave?.Radius ?? 0), (float)(wave?.Separation ?? 0));
        var region = frame.TextRegion;
        gl.Uniform4(_program.Uniform("uShield"),
            region is { } r0 ? (float)(r0.Cx * aspect) : 0,
            region is { } r1 ? (float)r1.Cy : 0,
            region is { } r2 ? (float)(r2.W * aspect / 2) : 1,
            region is { } r3 ? (float)(r3.H / 2) : 1);
        gl.Uniform4(_program.Uniform("uShieldB"), region is not null && wave is not null ? (float)(wave.Shield ?? 0.45) : 0, 0, 0, 0);

        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        gl.BindVertexArray(_vao);
        gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
    }

    private static void Set(float[] target, int offset, double x, double y, double z, double w)
    {
        target[offset] = (float)x;
        target[offset + 1] = (float)y;
        target[offset + 2] = (float)z;
        target[offset + 3] = (float)w;
    }

    private unsafe void UploadArray(string name, float[] values)
    {
        fixed (float* pointer = values)
            _gl.Uniform4(_program.Uniform(name), LumiereLight.MaxBeams, pointer);
    }

    public void Dispose()
    {
        _program.Dispose();
        _gl.DeleteVertexArray(_vao);
    }
}
