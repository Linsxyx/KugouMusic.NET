using System.Numerics;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects.Lumiere.Light;

public readonly record struct LumiereBloomPreset(float Strength, float Threshold, float Knee, int Levels, float Spread)
{
    /// <summary>Graphics group: a high threshold so only sources and beam cores glow, and text stays readable in the light.</summary>
    public static LumiereBloomPreset Graphics { get; } = new(1.5f, 0.42f, 0.3f, 6, 0.9f);
    /// <summary>Text group.</summary>
    public static LumiereBloomPreset Text { get; } = new(1.15f, 0.12f, 0.2f, 5, 0.95f);
}

/// <summary>
/// Folia lumiere/light/bloomFilter.ts, applied to one group texture: bright pass (threshold + soft knee, only on
/// the first level) → half-resolution dual-filter downsamples → tent upsamples that ADD the same level
/// (not mix, so octaves accumulate) → combine onto the parent target as premultiplied light.
/// This differs from <see cref="PostProcessPipeline"/>'s bloom on purpose; it is Lumiere's main look.
/// </summary>
internal sealed class LumiereBloom : IDisposable
{
    private const string Vertex = """
        #version 330 core
        out vec2 vUv;
        void main() {
            vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
            vUv = p;
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string Down = """
        #version 330 core
        in vec2 vUv;
        out vec4 finalColor;
        uniform sampler2D uTexture;
        uniform vec2 uTexel;     // one texel of the input
        uniform vec4 uParams;    // threshold, knee, prefilter(0/1), 0
        void main() {
            vec2 hp = uTexel;
            vec4 sum = texture(uTexture, vUv) * 4.0;
            sum += texture(uTexture, vUv - hp);
            sum += texture(uTexture, vUv + hp);
            sum += texture(uTexture, vUv + vec2(hp.x, -hp.y));
            sum += texture(uTexture, vUv - vec2(hp.x, -hp.y));
            vec4 c = sum / 8.0;
            if (uParams.z > 0.5) {
                float br = max(c.r, max(c.g, c.b));
                float knee = max(uParams.y, 1e-4);
                float soft = clamp(br - uParams.x + knee, 0.0, 2.0 * knee);
                soft = soft * soft / (4.0 * knee);
                float contrib = max(soft, br - uParams.x) / max(br, 1e-4);
                c *= clamp(contrib, 0.0, 1.0);
            }
            finalColor = c;
        }
        """;

    private const string Up = """
        #version 330 core
        in vec2 vUv;
        out vec4 finalColor;
        uniform sampler2D uTexture;
        uniform sampler2D uAdd;
        uniform vec2 uTexel;     // one texel of the (smaller) input
        uniform float uSpread;
        void main() {
            vec2 hp = uTexel;
            vec4 sum = texture(uTexture, vUv + vec2(-hp.x * 2.0, 0.0));
            sum += texture(uTexture, vUv + vec2(-hp.x, hp.y)) * 2.0;
            sum += texture(uTexture, vUv + vec2(0.0, hp.y * 2.0));
            sum += texture(uTexture, vUv + vec2(hp.x, hp.y)) * 2.0;
            sum += texture(uTexture, vUv + vec2(hp.x * 2.0, 0.0));
            sum += texture(uTexture, vUv + vec2(hp.x, -hp.y)) * 2.0;
            sum += texture(uTexture, vUv + vec2(0.0, -hp.y * 2.0));
            sum += texture(uTexture, vUv + vec2(-hp.x, -hp.y)) * 2.0;
            finalColor = sum / 12.0 + texture(uAdd, vUv) * uSpread;
        }
        """;

    private const string Combine = """
        #version 330 core
        in vec2 vUv;
        out vec4 finalColor;
        uniform sampler2D uTexture;
        uniform sampler2D uAdd;
        uniform vec4 uMap;       // output uv -> source uv: scale xy, offset zw
        uniform float uStrength;
        uniform vec3 uTint;
        uniform float uAlpha;
        void main() {
            vec2 uv = vUv * uMap.xy + uMap.zw;
            vec4 base = texture(uTexture, uv);
            vec4 glow = texture(uAdd, uv) * uStrength;
            vec3 g = glow.rgb * uTint;
            // The added glow is light too: premultiplied, alpha = max channel (like the light field).
            vec3 rgb = base.rgb + g * (1.0 - base.a * 0.5);
            // Above 1 scale back by the max channel (keeps the gold); only a little turns white.
            float peak = max(max(rgb.r, rgb.g), rgb.b);
            if (peak > 1.0) rgb = mix(rgb / peak, vec3(1.0), 0.2 * (1.0 - 1.0 / peak));
            float a = base.a + max(max(g.r, g.g), g.b) * (1.0 - base.a);
            finalColor = vec4(rgb, min(max(a, max(max(rgb.r, rgb.g), rgb.b)), 1.0)) * uAlpha;
        }
        """;

    private readonly GL _gl;
    private readonly EffectShaderProgram _down;
    private readonly EffectShaderProgram _up;
    private readonly EffectShaderProgram _combine;
    private readonly uint _vao;
    private readonly List<LumiereRenderTarget> _chain = [];
    private readonly List<LumiereRenderTarget> _scratch = [];

    public LumiereBloom(GL gl)
    {
        _gl = gl;
        _down = new EffectShaderProgram(gl, Vertex, Down, "lumiere-bloom-down");
        _up = new EffectShaderProgram(gl, Vertex, Up, "lumiere-bloom-up");
        _combine = new EffectShaderProgram(gl, Vertex, Combine, "lumiere-bloom-combine");
        _vao = gl.GenVertexArray();
    }

    public int DrawCalls { get; private set; }

    /// <summary>
    /// Blooms <paramref name="source"/> and composites it (premultiplied over) onto <paramref name="output"/>.
    /// <paramref name="map"/> maps the output's 0..1 uv onto the source's uv (crop of an overscanned group).
    /// </summary>
    public void Apply(LumiereRenderTarget source, LumiereBinding output, LumiereBloomPreset preset, float strengthScale,
        Vector4 map, float alpha = 1, Vector3? tint = null)
    {
        var gl = _gl;
        DrawCalls = 0;
        gl.Disable(EnableCap.Blend);
        gl.BindVertexArray(_vao);
        var strength = preset.Strength * strengthScale;
        uint glowTexture = source.Texture;
        if (strength > 0)
        {
            var width = source.Width;
            var height = source.Height;
            var count = Math.Max(1, Math.Min(8, preset.Levels));
            var built = 0;
            var input = source;
            _down.Use();
            gl.Uniform1(_down.Uniform("uTexture"), 0);
            gl.ActiveTexture(TextureUnit.Texture0);
            for (var level = 1; level <= count; level++)
            {
                var scale = 1.0 / (1 << level);
                if (Math.Min(width, height) * scale < 2) break;
                var target = Level(_chain, built++);
                target.EnsureSize((int)Math.Ceiling(width * scale - 1e-6), (int)Math.Ceiling(height * scale - 1e-6));
                target.Bind(false);
                gl.BindTexture(TextureTarget.Texture2D, input.Texture);
                gl.Uniform2(_down.Uniform("uTexel"), 1f / input.Width, 1f / input.Height);
                gl.Uniform4(_down.Uniform("uParams"), preset.Threshold, preset.Knee, level == 1 ? 1 : 0, 0);
                Draw();
                input = target;
            }

            var current = built > 0 ? _chain[built - 1] : source;
            _up.Use();
            gl.Uniform1(_up.Uniform("uTexture"), 0);
            gl.Uniform1(_up.Uniform("uAdd"), 1);
            gl.Uniform1(_up.Uniform("uSpread"), preset.Spread);
            for (var index = built - 2; index >= 0; index--)
            {
                var same = _chain[index];
                var target = Level(_scratch, index);
                target.EnsureSize(same.Width, same.Height);
                target.Bind(false);
                gl.ActiveTexture(TextureUnit.Texture0);
                gl.BindTexture(TextureTarget.Texture2D, current.Texture);
                gl.ActiveTexture(TextureUnit.Texture1);
                gl.BindTexture(TextureTarget.Texture2D, same.Texture);
                gl.Uniform2(_up.Uniform("uTexel"), 1f / current.Width, 1f / current.Height);
                Draw();
                current = target;
            }
            glowTexture = current.Texture;
        }

        output.Restore(gl);
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _combine.Use();
        gl.Uniform1(_combine.Uniform("uTexture"), 0);
        gl.Uniform1(_combine.Uniform("uAdd"), 1);
        gl.Uniform4(_combine.Uniform("uMap"), map.X, map.Y, map.Z, map.W);
        gl.Uniform1(_combine.Uniform("uStrength"), Math.Max(0, strength));
        var color = tint ?? Vector3.One;
        gl.Uniform3(_combine.Uniform("uTint"), color.X, color.Y, color.Z);
        gl.Uniform1(_combine.Uniform("uAlpha"), alpha);
        gl.ActiveTexture(TextureUnit.Texture1);
        gl.BindTexture(TextureTarget.Texture2D, glowTexture);
        gl.ActiveTexture(TextureUnit.Texture0);
        gl.BindTexture(TextureTarget.Texture2D, source.Texture);
        Draw();
    }

    private LumiereRenderTarget Level(List<LumiereRenderTarget> list, int index)
    {
        while (list.Count <= index) list.Add(new LumiereRenderTarget(_gl));
        return list[index];
    }

    private void Draw()
    {
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        DrawCalls++;
    }

    public void Dispose()
    {
        foreach (var target in _chain) target.Dispose();
        foreach (var target in _scratch) target.Dispose();
        _chain.Clear();
        _scratch.Clear();
        _down.Dispose();
        _up.Dispose();
        _combine.Dispose();
        _gl.DeleteVertexArray(_vao);
    }
}
