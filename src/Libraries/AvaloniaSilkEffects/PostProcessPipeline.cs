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

    // Dual-filter (Bjørge 2015) downsample: a 5-tap box around the half-texel
    // corners. The first bloom level also applies a soft-knee brightness gate.
    private const string DownFragment = """
        #version 330 core
        in vec2 vUv;
        uniform sampler2D uTexture;
        uniform vec2 uHalfTexel;
        uniform int uPrefilter;
        uniform float uThreshold;
        uniform float uKnee;
        out vec4 finalColor;
        // Gate each tap before averaging: thin strokes are only a texel or two
        // wide and would fall under the threshold once mixed with the backdrop.
        vec4 tap(vec2 uv) {
            vec4 c = texture(uTexture, uv);
            if (uPrefilter == 0) return c;
            float brightness = max(c.r, max(c.g, c.b));
            float soft = clamp(brightness - uThreshold + uKnee, 0.0, 2.0 * uKnee);
            soft = soft * soft / (4.0 * uKnee + 1e-4);
            float contribution = max(soft, brightness - uThreshold) / max(brightness, 1e-4);
            return vec4(c.rgb * contribution, 0.0);
        }
        void main() {
            vec4 c = tap(vUv) * 4.0;
            c += tap(vUv - uHalfTexel);
            c += tap(vUv + uHalfTexel);
            c += tap(vUv + vec2(uHalfTexel.x, -uHalfTexel.y));
            c += tap(vUv - vec2(uHalfTexel.x, -uHalfTexel.y));
            c *= 0.125;
            finalColor = c;
        }
        """;

    // Dual-filter upsample: an 8-tap tent from the coarser level, blended with
    // the same-resolution down level so every octave contributes (uScatter < 1).
    private const string UpFragment = """
        #version 330 core
        in vec2 vUv;
        uniform sampler2D uTexture;
        uniform sampler2D uBase;
        uniform vec2 uHalfTexel;
        uniform float uSpread;
        uniform float uScatter;
        out vec4 finalColor;
        void main() {
            vec2 h = uHalfTexel * uSpread;
            vec4 c = texture(uTexture, vUv + vec2(-h.x * 2.0, 0.0));
            c += texture(uTexture, vUv + vec2(-h.x, h.y)) * 2.0;
            c += texture(uTexture, vUv + vec2(0.0, h.y * 2.0));
            c += texture(uTexture, vUv + vec2(h.x, h.y)) * 2.0;
            c += texture(uTexture, vUv + vec2(h.x * 2.0, 0.0));
            c += texture(uTexture, vUv + vec2(h.x, -h.y)) * 2.0;
            c += texture(uTexture, vUv + vec2(0.0, -h.y * 2.0));
            c += texture(uTexture, vUv + vec2(-h.x, -h.y)) * 2.0;
            c /= 12.0;
            finalColor = mix(texture(uBase, vUv), c, uScatter);
        }
        """;

    // Separable horizontal gaussian used to stretch bloom into an anamorphic streak.
    private const string StreakFragment = """
        #version 330 core
        in vec2 vUv;
        uniform sampler2D uTexture;
        uniform vec2 uStep;
        out vec4 finalColor;
        void main() {
            vec4 c = texture(uTexture, vUv) * 0.2270270;
            c += (texture(uTexture, vUv + uStep * 1.3846154) + texture(uTexture, vUv - uStep * 1.3846154)) * 0.3162162;
            c += (texture(uTexture, vUv + uStep * 3.2307692) + texture(uTexture, vUv - uStep * 3.2307692)) * 0.0702703;
            finalColor = c;
        }
        """;

    private const string CompositeFragment = """
        #version 330 core
        in vec2 vUv;
        uniform sampler2D uSource;
        uniform sampler2D uBlurred;
        uniform sampler2D uBloomTexture;
        uniform sampler2D uStreakTexture;
        uniform vec2 uResolution;
        uniform float uBloom;
        uniform vec3 uBloomTint;
        uniform float uStreak;
        uniform float uZoomBlur;
        uniform vec2 uZoomCenter;
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
        uniform int uMonoGlitch;
        uniform float uTime;
        uniform float uSeed;
        uniform mat4 uColorMatrix;
        out vec4 finalColor;

        float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233)) + uSeed) * 43758.5453); }

        float glitchHash(vec2 value) { return fract(sin(dot(value, vec2(12.9898, 78.233))) * 43758.5453); }

        vec2 distort(vec2 uv) {
            vec2 p = uv * 2.0 - 1.0;
            float r2 = dot(p, p);
            p *= 1.0 + r2 * uLensDistortion * 0.18;
            return p * 0.5 + 0.5;
        }

        void main() {
            vec2 uv = distort(vUv);
            float glitchGain = 0.0;
            if (uMonoGlitch == 1) {
                // Folia sonnetGlitchFilter: coarse and fine horizontal slices picked by the
                // transition's stepped seed, plus equal-channel brightness tears (no RGB split).
                // Pixi filter coordinates run top-down.
                float y = 1.0 - uv.y;
                float coarseBand = floor(y * 26.0);
                float fineBand = floor(y * 110.0);
                float coarseNoise = glitchHash(vec2(coarseBand, uSeed));
                float fineNoise = glitchHash(vec2(fineBand + 41.0, uSeed * 1.37));
                float coarseGate = step(0.58, coarseNoise);
                float fineGate = step(0.88, fineNoise);
                uv.x += (glitchHash(vec2(coarseBand + 17.0, uSeed)) * 2.0 - 1.0) * coarseGate * uGlitch * 0.095
                    + (glitchHash(vec2(fineBand + 73.0, uSeed)) * 2.0 - 1.0) * fineGate * uGlitch * 0.035;
                glitchGain = (coarseGate * (coarseNoise - 0.58) + fineGate * 0.12) * uGlitch * 0.42;
            } else {
                float band = floor(uv.y * 48.0);
                uv.x += step(0.72, hash(vec2(band, floor(uTime * 18.0)))) *
                    (hash(vec2(band + 17.0, uSeed)) * 2.0 - 1.0) * uGlitch * 0.075;
            }
            vec2 dispersion = vec2((uRgbSplit + uLensDispersion) * 0.006, 0.0);
            vec4 color;
            if (uZoomBlur > 0.001) {
                // Radial smear toward the focus point; channels sample slightly
                // different lengths so the streaks pick up a faint prism fringe.
                vec4 sum = vec4(0.0);
                float total = 0.0;
                vec2 ray = uv - uZoomCenter;
                for (int i = 0; i < 16; i++) {
                    float t = float(i) / 15.0;
                    float w = 1.0 - t * 0.6;
                    float k = 1.0 - uZoomBlur * 0.12 * t;
                    vec4 s = texture(uSource, clamp(uZoomCenter + ray * k, 0.0, 1.0));
                    s.r = texture(uSource, clamp(uZoomCenter + ray * (k + uZoomBlur * 0.004 * t), 0.0, 1.0)).r;
                    sum += s * w;
                    total += w;
                }
                color = sum / total;
            } else {
                color = texture(uSource, clamp(uv, 0.0, 1.0));
                color.r = texture(uSource, clamp(uv + dispersion, 0.0, 1.0)).r;
                color.b = texture(uSource, clamp(uv - dispersion, 0.0, 1.0)).b;
            }
            color.rgb *= 1.0 + glitchGain;
            vec4 blurred = texture(uBlurred, clamp(uv, 0.0, 1.0));
            color = mix(color, blurred, clamp(uBlur, 0.0, 1.0));
            color.rgb += blurred.rgb * max(0.0, uGlow);
            if (uBloom > 0.0 || uStreak > 0.0) {
                vec3 light = texture(uBloomTexture, clamp(uv, 0.0, 1.0)).rgb * uBloom +
                    texture(uStreakTexture, clamp(uv, 0.0, 1.0)).rgb * uStreak;
                light *= uBloomTint;
                // Light is emitted, so it also covers a transparent clear; keep the
                // output a valid premultiplied color by raising alpha with it.
                color.rgb += light;
                color.a = max(color.a, min(1.0, max(light.r, max(light.g, light.b))));
            }
            color = uColorMatrix * color;
            color.rgb = (color.rgb - 0.5 * color.a) * (1.0 + uContrast) + 0.5 * color.a;
            float dots = sin(uv.x * uResolution.x * 0.42) * sin(uv.y * uResolution.y * 0.42);
            color.rgb *= 1.0 - uHalftone * (0.08 + 0.08 * dots);
            float noise = hash(gl_FragCoord.xy + floor(uTime * 60.0)) - 0.5;
            color.rgb += noise * uGrain * 0.12 * color.a;
            vec2 edge = abs(vUv * 2.0 - 1.0);
            float vignette = smoothstep(0.45, 1.18, length(edge));
            color.rgb *= 1.0 - vignette * uVignette * 0.72;
            color = clamp(color, 0.0, 1.0);
            // Fringes and channel splits can push one channel past the coverage
            // they were sampled from; keep the output valid premultiplied color.
            color.rgb = min(color.rgb, vec3(color.a));
            finalColor = color;
        }
        """;

    private readonly GL _gl;
    private readonly EffectFramebuffer _source;
    private readonly EffectFramebuffer _multisampleSource;
    private readonly int _maxSamples;
    private readonly bool _isGles;
    private const int BloomLevels = 6;
    private const int BlurLevels = 4;
    private readonly EffectFramebuffer[] _bloomDown;
    private readonly EffectFramebuffer[] _bloomUp;
    private readonly EffectFramebuffer[] _blurDown;
    private readonly EffectFramebuffer[] _blurUp;
    private readonly EffectFramebuffer _streakPing;
    private readonly EffectFramebuffer _streakPong;
    private readonly EffectShaderProgram _downShader;
    private readonly EffectShaderProgram _upShader;
    private readonly EffectShaderProgram _streakShader;
    private readonly EffectShaderProgram _compositeShader;
    private readonly uint _vao;
    private EffectFramebuffer? _sonnetPing;
    private EffectFramebuffer? _sonnetPong;
    private EffectShaderProgram? _sonnetShader;
    private int _targetWidth;
    private int _targetHeight;
    private bool _usesOffscreen;

    public int FrameDrawCalls { get; private set; }
    public int MultisampleCount { get; private set; } = 1;

    public PostProcessPipeline(GL gl)
    {
        _gl = gl;
        _source = new EffectFramebuffer(gl);
        _multisampleSource = new EffectFramebuffer(gl);
        gl.GetInteger(GLEnum.MaxSamples, out _maxSamples);
        _isGles = gl.GetStringS(StringName.Version).Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase);
        _bloomDown = CreateChain(gl, BloomLevels);
        _bloomUp = CreateChain(gl, BloomLevels);
        _blurDown = CreateChain(gl, BlurLevels);
        _blurUp = CreateChain(gl, BlurLevels);
        _streakPing = new EffectFramebuffer(gl);
        _streakPong = new EffectFramebuffer(gl);
        _downShader = new EffectShaderProgram(gl, FullscreenVertex, DownFragment, "effects-dual-down");
        _upShader = new EffectShaderProgram(gl, FullscreenVertex, UpFragment, "effects-dual-up");
        _streakShader = new EffectShaderProgram(gl, FullscreenVertex, StreakFragment, "effects-streak");
        _compositeShader = new EffectShaderProgram(gl, FullscreenVertex, CompositeFragment, "effects-composite");
        _vao = gl.GenVertexArray();
    }

    public void Begin(
        int width,
        int height,
        int targetFramebuffer,
        EffectColor clearColor,
        bool enabled,
        float resolutionScale,
        int multisampleCount = 1)
    {
        FrameDrawCalls = 0;
        _targetWidth = width;
        _targetHeight = height;
        var samples = Math.Clamp(multisampleCount, 1, Math.Max(1, _maxSamples));
        _usesOffscreen = enabled || samples > 1;
        MultisampleCount = 1;
        var renderWidth = width;
        var renderHeight = height;
        if (_usesOffscreen)
        {
            var scale = enabled ? Math.Clamp(resolutionScale, 0.25f, 1f) : 1;
            renderWidth = Math.Max(1, (int)MathF.Ceiling(width * scale));
            renderHeight = Math.Max(1, (int)MathF.Ceiling(height * scale));
            _source.EnsureSize(renderWidth, renderHeight);
        }
        if (samples > 1)
        {
            _multisampleSource.EnsureSize(renderWidth, renderHeight, samples);
            MultisampleCount = _multisampleSource.Samples;
            // GLES always rasterizes multisampled attachments with MSAA; it has
            // no GL_MULTISAMPLE capability. Desktop GL needs this per-frame state.
            if (!_isGles) _gl.Enable(EnableCap.Multisample);
        }
        else
            _multisampleSource.Dispose();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer,
            samples > 1 ? _multisampleSource.Framebuffer : _usesOffscreen ? _source.Framebuffer : (uint)targetFramebuffer);
        _gl.Viewport(0, 0, (uint)renderWidth, (uint)renderHeight);
        var clear = clearColor.Premultiplied();
        _gl.ClearColor(clear.R, clear.G, clear.B, clear.A);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.StencilBufferBit);
    }

    public unsafe void End(int targetFramebuffer, PostProcessSettings settings)
    {
        if (!_usesOffscreen)
            return;

        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.StencilTest);
        // Resolve geometry before any filter samples it. Fullscreen filters
        // then preserve coverage, including premultiplied RGB and alpha.
        if (MultisampleCount > 1)
            _multisampleSource.ResolveTo(_source);
        _gl.BindVertexArray(_vao);

        var sourceTexture = _source.Texture;
        if (settings.UseSonnetPasses)
        {
            _sonnetShader ??= new EffectShaderProgram(_gl, FullscreenVertex, Sonnet.SonnetFilterShader.Fragment, "sonnet-material");
            _sonnetPing ??= new EffectFramebuffer(_gl);
            _sonnetPong ??= new EffectFramebuffer(_gl);
            ReadOnlySpan<float> amounts = [settings.LensDistortion, settings.Grain, settings.Contrast,
                settings.RgbSplit, settings.Halftone, settings.Vignette];
            var count = 0;
            for (var pass = 0; pass < amounts.Length; pass++)
            {
                if (amounts[pass] <= 0 && !(pass == 0 && settings.LensDispersion > 0)) continue;
                var destination = count++ % 2 == 0 ? _sonnetPing : _sonnetPong;
                destination.EnsureSize(_source.Width, _source.Height);
                _gl.BindFramebuffer(FramebufferTarget.Framebuffer, destination.Framebuffer);
                _gl.Viewport(0, 0, (uint)_source.Width, (uint)_source.Height);
                _sonnetShader.Use();
                BindTexture(_sonnetShader, "uTexture", sourceTexture, TextureUnit.Texture0, 0);
                _gl.Uniform2(_sonnetShader.Uniform("uResolution"), (float)_source.Width, (float)_source.Height);
                _gl.Uniform1(_sonnetShader.Uniform("uPass"), pass);
                _gl.Uniform1(_sonnetShader.Uniform("uAmount"), amounts[pass]);
                _gl.Uniform1(_sonnetShader.Uniform("uDispersion"), settings.LensDispersion);
                _gl.Uniform1(_sonnetShader.Uniform("uSeed"), settings.SonnetNoiseSeed);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
                FrameDrawCalls++;
                sourceTexture = destination.Texture;
            }
        }

        var blurEnabled = settings.Blur > 0.001f || settings.Glow > 0.001f;
        var blurredTexture = sourceTexture;
        if (blurEnabled)
        {
            // Wider transitions open the tent further instead of switching octave
            // counts, so an animated blur amount never pops between radii.
            var spread = Math.Clamp(0.6f + settings.Blur * 0.9f + settings.Glow * 0.4f, 0.6f, 1.5f);
            blurredTexture = DualFilter(sourceTexture, _source.Width, _source.Height, _blurDown, _blurUp,
                BlurLevels, prefilter: false, threshold: 0, spread, scatter: 1);
        }

        var bloomEnabled = settings.Bloom > 0.001f || settings.BloomStreak > 0.001f;
        var bloomTexture = sourceTexture;
        var streakTexture = sourceTexture;
        if (bloomEnabled)
        {
            var radius = Math.Clamp(settings.BloomRadius, 0, 1);
            bloomTexture = DualFilter(sourceTexture, _source.Width, _source.Height, _bloomDown, _bloomUp,
                BloomLevels, prefilter: true, Math.Clamp(settings.BloomThreshold, 0, 1),
                spread: 1, scatter: 0.45f + radius * 0.45f);
            if (settings.BloomStreak > 0.001f && _bloomDown[1].Framebuffer != 0)
                streakTexture = Streak(_bloomDown[1]);
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)targetFramebuffer);
        _gl.Viewport(0, 0, (uint)_targetWidth, (uint)_targetHeight);
        _compositeShader.Use();
        BindTexture(_compositeShader, "uSource", sourceTexture, TextureUnit.Texture0, 0);
        BindTexture(_compositeShader, "uBlurred", blurredTexture, TextureUnit.Texture1, 1);
        BindTexture(_compositeShader, "uBloomTexture", bloomTexture, TextureUnit.Texture2, 2);
        BindTexture(_compositeShader, "uStreakTexture", streakTexture, TextureUnit.Texture3, 3);
        Set("uBloom", bloomEnabled ? settings.Bloom : 0);
        Set("uStreak", bloomEnabled && settings.BloomStreak > 0.001f ? settings.BloomStreak : 0);
        _gl.Uniform3(_compositeShader.Uniform("uBloomTint"), settings.BloomTint.R, settings.BloomTint.G, settings.BloomTint.B);
        Set("uZoomBlur", Math.Clamp(settings.ZoomBlur, 0, 2));
        _gl.Uniform2(_compositeShader.Uniform("uZoomCenter"), settings.ZoomBlurCenter.X, 1 - settings.ZoomBlurCenter.Y);
        _gl.Uniform2(_compositeShader.Uniform("uResolution"), (float)_source.Width, (float)_source.Height);
        Set("uBlur", settings.Blur);
        Set("uGlow", settings.Glow);
        Set("uGrain", settings.UseSonnetPasses ? 0 : settings.Grain);
        Set("uContrast", settings.UseSonnetPasses ? 0 : settings.Contrast);
        Set("uRgbSplit", settings.UseSonnetPasses ? 0 : settings.RgbSplit);
        Set("uHalftone", settings.UseSonnetPasses ? 0 : settings.Halftone);
        Set("uVignette", settings.UseSonnetPasses ? 0 : settings.Vignette);
        Set("uLensDistortion", settings.UseSonnetPasses ? 0 : settings.LensDistortion);
        Set("uLensDispersion", settings.UseSonnetPasses ? 0 : settings.LensDispersion);
        Set("uGlitch", settings.Glitch);
        _gl.Uniform1(_compositeShader.Uniform("uMonoGlitch"), settings.UseSonnetPasses ? 1 : 0);
        Set("uTime", settings.Time);
        Set("uSeed", settings.Seed);
        var matrix = settings.ColorMatrix;
        _gl.UniformMatrix4(_compositeShader.Uniform("uColorMatrix"), 1, true, (float*)&matrix);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        FrameDrawCalls++;
        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    private static EffectFramebuffer[] CreateChain(GL gl, int levels)
    {
        var chain = new EffectFramebuffer[levels];
        for (var index = 0; index < levels; index++)
            chain[index] = new EffectFramebuffer(gl);
        return chain;
    }

    /// <summary>
    /// Runs a dual-filter pyramid starting at half resolution and returns the
    /// texture of the finest upsampled level.
    /// </summary>
    private uint DualFilter(uint source, int width, int height, EffectFramebuffer[] down, EffectFramebuffer[] up,
        int levels, bool prefilter, float threshold, float spread, float scatter)
    {
        var input = source;
        var inputWidth = width;
        var inputHeight = height;
        var used = 0;
        _downShader.Use();
        _gl.Uniform1(_downShader.Uniform("uThreshold"), threshold);
        _gl.Uniform1(_downShader.Uniform("uKnee"), MathF.Max(0.04f, (1 - threshold) * 0.5f));
        for (var level = 0; level < levels; level++)
        {
            var levelWidth = Math.Max(1, inputWidth / 2);
            var levelHeight = Math.Max(1, inputHeight / 2);
            down[level].EnsureSize(levelWidth, levelHeight);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, down[level].Framebuffer);
            _gl.Viewport(0, 0, (uint)levelWidth, (uint)levelHeight);
            BindTexture(_downShader, "uTexture", input, TextureUnit.Texture0, 0);
            _gl.Uniform2(_downShader.Uniform("uHalfTexel"), 0.5f / inputWidth, 0.5f / inputHeight);
            _gl.Uniform1(_downShader.Uniform("uPrefilter"), prefilter && level == 0 ? 1 : 0);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            FrameDrawCalls++;
            input = down[level].Texture;
            inputWidth = levelWidth;
            inputHeight = levelHeight;
            used++;
            if (levelWidth <= 8 || levelHeight <= 8) break;
        }

        _upShader.Use();
        _gl.Uniform1(_upShader.Uniform("uSpread"), spread);
        _gl.Uniform1(_upShader.Uniform("uScatter"), scatter);
        var lower = down[used - 1];
        for (var level = used - 2; level >= 0; level--)
        {
            var target = up[level];
            target.EnsureSize(down[level].Width, down[level].Height);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, target.Framebuffer);
            _gl.Viewport(0, 0, (uint)target.Width, (uint)target.Height);
            BindTexture(_upShader, "uTexture", lower.Texture, TextureUnit.Texture0, 0);
            BindTexture(_upShader, "uBase", down[level].Texture, TextureUnit.Texture1, 1);
            _gl.Uniform2(_upShader.Uniform("uHalfTexel"), 0.5f / lower.Width, 0.5f / lower.Height);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            FrameDrawCalls++;
            lower = target;
        }
        return lower.Texture;
    }

    private uint Streak(EffectFramebuffer bright)
    {
        // Squash vertically so a few horizontal taps cover a wide, thin flare.
        var width = Math.Max(1, bright.Width / 2);
        var height = Math.Max(1, bright.Height / 6);
        _streakPing.EnsureSize(width, height);
        _streakPong.EnsureSize(width, height);
        _streakShader.Use();
        var input = bright.Texture;
        ReadOnlySpan<float> steps = [1, 3, 9, 27];
        for (var pass = 0; pass < steps.Length; pass++)
        {
            var target = pass % 2 == 0 ? _streakPing : _streakPong;
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, target.Framebuffer);
            _gl.Viewport(0, 0, (uint)width, (uint)height);
            BindTexture(_streakShader, "uTexture", input, TextureUnit.Texture0, 0);
            _gl.Uniform2(_streakShader.Uniform("uStep"), steps[pass] / width, 0f);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            FrameDrawCalls++;
            input = target.Texture;
        }
        return input;
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
        _sonnetShader?.Dispose();
        _sonnetPing?.Dispose();
        _sonnetPong?.Dispose();
        _gl.DeleteVertexArray(_vao);
        _downShader.Dispose();
        _upShader.Dispose();
        _streakShader.Dispose();
        _compositeShader.Dispose();
        _source.Dispose();
        _multisampleSource.Dispose();
        foreach (var framebuffer in _bloomDown.Concat(_bloomUp).Concat(_blurDown).Concat(_blurUp))
            framebuffer.Dispose();
        _streakPing.Dispose();
        _streakPong.Dispose();
    }
}
