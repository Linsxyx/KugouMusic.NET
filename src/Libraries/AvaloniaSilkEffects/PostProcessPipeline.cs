using System.Numerics;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects;

internal sealed class PostProcessPipeline : IDisposable
{
    private const string FullscreenVertex = """
        #version 330 core
        out vec2 vUv;
        void main() {
            vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
            vUv = p;
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string BlurFragment = """
        #version 330 core
        in vec2 vUv;
        uniform sampler2D uTexture;
        uniform vec2 uTexel;
        uniform float uRadius;
        out vec4 finalColor;
        void main() {
            vec2 stepUv = uTexel * max(0.5, uRadius);
            vec4 c = texture(uTexture, vUv) * 0.227027;
            c += texture(uTexture, vUv + vec2(stepUv.x, 0.0)) * 0.1945946;
            c += texture(uTexture, vUv - vec2(stepUv.x, 0.0)) * 0.1945946;
            c += texture(uTexture, vUv + vec2(0.0, stepUv.y)) * 0.1216216;
            c += texture(uTexture, vUv - vec2(0.0, stepUv.y)) * 0.1216216;
            c += texture(uTexture, vUv + stepUv) * 0.0702703;
            c += texture(uTexture, vUv - stepUv) * 0.0702703;
            finalColor = c;
        }
        """;

    private const string CompositeFragment = """
        #version 330 core
        in vec2 vUv;
        uniform sampler2D uSource;
        uniform sampler2D uBlurred;
        uniform vec2 uResolution;
        uniform float uBlur;
        uniform float uGlow;
        uniform float uGrain;
        uniform float uContrast;
        uniform float uRgbSplit;
        uniform float uHalftone;
        uniform float uVignette;
        uniform float uLensDistortion;
        uniform float uLensDispersion;
        uniform float uGlitch;
        uniform float uTime;
        uniform float uSeed;
        uniform mat4 uColorMatrix;
        out vec4 finalColor;

        float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233)) + uSeed) * 43758.5453); }

        vec2 distort(vec2 uv) {
            vec2 p = uv * 2.0 - 1.0;
            float r2 = dot(p, p);
            p *= 1.0 + r2 * uLensDistortion * 0.18;
            return p * 0.5 + 0.5;
        }

        void main() {
            vec2 uv = distort(vUv);
            float band = floor(uv.y * 48.0);
            float tear = step(0.72, hash(vec2(band, floor(uTime * 18.0)))) *
                (hash(vec2(band + 17.0, uSeed)) * 2.0 - 1.0) * uGlitch * 0.075;
            uv.x += tear;
            vec2 dispersion = vec2((uRgbSplit + uLensDispersion) * 0.006, 0.0);
            vec4 center = texture(uSource, clamp(uv, 0.0, 1.0));
            vec4 color = center;
            color.r = texture(uSource, clamp(uv + dispersion, 0.0, 1.0)).r;
            color.b = texture(uSource, clamp(uv - dispersion, 0.0, 1.0)).b;
            vec4 blurred = texture(uBlurred, clamp(uv, 0.0, 1.0));
            color = mix(color, blurred, clamp(uBlur, 0.0, 1.0));
            color.rgb += blurred.rgb * max(0.0, uGlow);
            color = uColorMatrix * color;
            color.rgb = (color.rgb - 0.5 * color.a) * (1.0 + uContrast) + 0.5 * color.a;
            float dots = sin(uv.x * uResolution.x * 0.42) * sin(uv.y * uResolution.y * 0.42);
            color.rgb *= 1.0 - uHalftone * (0.08 + 0.08 * dots);
            float noise = hash(gl_FragCoord.xy + floor(uTime * 60.0)) - 0.5;
            color.rgb += noise * uGrain * 0.12 * color.a;
            vec2 edge = abs(vUv * 2.0 - 1.0);
            float vignette = smoothstep(0.45, 1.18, length(edge));
            color.rgb *= 1.0 - vignette * uVignette * 0.72;
            finalColor = clamp(color, 0.0, 1.0);
        }
        """;

    private readonly GL _gl;
    private readonly EffectFramebuffer _source;
    private readonly EffectFramebuffer _blurred;
    private readonly EffectShaderProgram _blurShader;
    private readonly EffectShaderProgram _compositeShader;
    private readonly uint _vao;
    private int _targetWidth;
    private int _targetHeight;

    public int FrameDrawCalls { get; private set; }

    public PostProcessPipeline(GL gl)
    {
        _gl = gl;
        _source = new(gl);
        _blurred = new(gl);
        _blurShader = new(gl, FullscreenVertex, BlurFragment, "effects-blur");
        _compositeShader = new(gl, FullscreenVertex, CompositeFragment, "effects-composite");
        _vao = gl.GenVertexArray();
    }

    public void Begin(
        int width,
        int height,
        int targetFramebuffer,
        EffectColor clearColor,
        bool enabled,
        float resolutionScale)
    {
        FrameDrawCalls = 0;
        _targetWidth = width;
        _targetHeight = height;
        var renderWidth = width;
        var renderHeight = height;
        if (enabled)
        {
            var scale = Math.Clamp(resolutionScale, 0.25f, 1f);
            renderWidth = Math.Max(1, (int)MathF.Ceiling(width * scale));
            renderHeight = Math.Max(1, (int)MathF.Ceiling(height * scale));
            _source.EnsureSize(renderWidth, renderHeight);
        }
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer,
            enabled ? _source.Framebuffer : (uint)targetFramebuffer);
        _gl.Viewport(0, 0, (uint)renderWidth, (uint)renderHeight);
        var clear = clearColor.Premultiplied();
        _gl.ClearColor(clear.R, clear.G, clear.B, clear.A);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.StencilBufferBit);
    }

    public unsafe void End(int targetFramebuffer, PostProcessSettings settings, bool enabled)
    {
        if (!enabled)
            return;

        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindVertexArray(_vao);

        var blurEnabled = settings.Blur > 0.001f || settings.Glow > 0.001f;
        if (blurEnabled)
        {
            _blurred.EnsureSize(_source.Width, _source.Height);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _blurred.Framebuffer);
            _gl.Viewport(0, 0, (uint)_source.Width, (uint)_source.Height);
            _blurShader.Use();
            BindTexture(_blurShader, "uTexture", _source.Texture, TextureUnit.Texture0, 0);
            _gl.Uniform2(_blurShader.Uniform("uTexel"), 1f / _source.Width, 1f / _source.Height);
            _gl.Uniform1(_blurShader.Uniform("uRadius"), MathF.Max(1, settings.Blur * 5 + settings.Glow * 4));
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            FrameDrawCalls++;
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)targetFramebuffer);
        _gl.Viewport(0, 0, (uint)_targetWidth, (uint)_targetHeight);
        _compositeShader.Use();
        BindTexture(_compositeShader, "uSource", _source.Texture, TextureUnit.Texture0, 0);
        BindTexture(_compositeShader, "uBlurred",
            blurEnabled ? _blurred.Texture : _source.Texture, TextureUnit.Texture1, 1);
        _gl.Uniform2(_compositeShader.Uniform("uResolution"), (float)_source.Width, (float)_source.Height);
        Set("uBlur", settings.Blur);
        Set("uGlow", settings.Glow);
        Set("uGrain", settings.Grain);
        Set("uContrast", settings.Contrast);
        Set("uRgbSplit", settings.RgbSplit);
        Set("uHalftone", settings.Halftone);
        Set("uVignette", settings.Vignette);
        Set("uLensDistortion", settings.LensDistortion);
        Set("uLensDispersion", settings.LensDispersion);
        Set("uGlitch", settings.Glitch);
        Set("uTime", settings.Time);
        Set("uSeed", settings.Seed);
        var matrix = settings.ColorMatrix;
        _gl.UniformMatrix4(_compositeShader.Uniform("uColorMatrix"), 1, true, (float*)&matrix);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        FrameDrawCalls++;
    }

    private void Set(string uniform, float value) =>
        _gl.Uniform1(_compositeShader.Uniform(uniform), value);

    private void BindTexture(EffectShaderProgram shader, string uniform, uint texture, TextureUnit unit, int index)
    {
        _gl.ActiveTexture(unit);
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.Uniform1(shader.Uniform(uniform), index);
    }

    public void Dispose()
    {
        _gl.DeleteVertexArray(_vao);
        _blurShader.Dispose();
        _compositeShader.Dispose();
        _source.Dispose();
        _blurred.Dispose();
    }
}
