using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects.Lumiere.Light;

/// <summary>
/// The runtime's layer frame (Folia lumiereSceneEntry.ts applyLumiereLayerFrame): a paragraph scene — or the credits card —
/// rendered into its own target, then composited with alpha, a scale about the frame center and an optional blur. The blur
/// stands in for Pixi's BlurFilter (quality 3, half resolution): a separable Gaussian at half resolution whose sigma follows
/// the strength, so it reads the same without matching Pixi's pass structure tap for tap.
/// </summary>
internal sealed class LumiereCompositor : IDisposable
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

    private const string Blit = """
        #version 330 core
        in vec2 vUv;
        out vec4 finalColor;
        uniform sampler2D uTexture;
        uniform vec4 uMap;      // uv scale xy, offset zw
        uniform float uAlpha;
        void main() {
            vec2 uv = vUv * uMap.xy + uMap.zw;
            if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) { finalColor = vec4(0.0); return; }
            finalColor = texture(uTexture, uv) * uAlpha;
        }
        """;

    private const string Gauss = """
        #version 330 core
        in vec2 vUv;
        out vec4 finalColor;
        uniform sampler2D uTexture;
        uniform vec2 uStep;     // one sigma along the axis, in uv
        void main() {
            // 9 taps at 0, ±0.75σ, ±1.5σ, ±2.25σ, ±3σ with Gaussian weights.
            float w[5] = float[](0.1784, 0.1543, 0.1006, 0.0494, 0.0182);
            vec4 sum = texture(uTexture, vUv) * w[0];
            for (int i = 1; i < 5; i++) {
                vec2 o = uStep * (0.75 * float(i));
                sum += (texture(uTexture, vUv + o) + texture(uTexture, vUv - o)) * w[i];
            }
            finalColor = sum / (w[0] + 2.0 * (w[1] + w[2] + w[3] + w[4]));
        }
        """;

    private readonly GL _gl;
    private readonly EffectShaderProgram _blit;
    private readonly EffectShaderProgram _gauss;
    private readonly uint _vao;

    public LumiereCompositor(GL gl)
    {
        _gl = gl;
        _blit = new EffectShaderProgram(gl, Vertex, Blit, "lumiere-layer-blit");
        _gauss = new EffectShaderProgram(gl, Vertex, Gauss, "lumiere-layer-blur");
        _vao = gl.GenVertexArray();
        Layer = new LumiereRenderTarget(gl);
        _blurA = new LumiereRenderTarget(gl);
        _blurB = new LumiereRenderTarget(gl);
    }

    /// <summary>The layer target a scene renders into before compositing (same size as the output).</summary>
    public LumiereRenderTarget Layer { get; }
    private readonly LumiereRenderTarget _blurA;
    private readonly LumiereRenderTarget _blurB;

    /// <summary>
    /// Composites <see cref="Layer"/> onto <paramref name="output"/>: alpha, scale about the center plus a vertical offset (as a
    /// fraction of the height), blur strength in output pixels.
    /// </summary>
    public void Composite(LumiereBinding output, float alpha, float scale, float blurPixels, float offsetY = 0)
    {
        var gl = _gl;
        gl.BindVertexArray(_vao);
        gl.ActiveTexture(TextureUnit.Texture0);
        var source = Layer.Texture;
        if (blurPixels > 0.3f)
        {
            _blurA.EnsureSize((Layer.Width + 1) / 2, (Layer.Height + 1) / 2);
            _blurB.EnsureSize(_blurA.Width, _blurA.Height);
            gl.Disable(EnableCap.Blend);
            _blurA.Bind(false);
            _blit.Use();
            gl.Uniform1(_blit.Uniform("uTexture"), 0);
            gl.Uniform4(_blit.Uniform("uMap"), 1f, 1f, 0f, 0f);
            gl.Uniform1(_blit.Uniform("uAlpha"), 1f);
            gl.BindTexture(TextureTarget.Texture2D, Layer.Texture);
            gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            // Strength → sigma in half-resolution pixels.
            var sigma = blurPixels * 0.5f * 0.5f;
            _gauss.Use();
            gl.Uniform1(_gauss.Uniform("uTexture"), 0);
            _blurB.Bind(false);
            gl.Uniform2(_gauss.Uniform("uStep"), sigma / _blurA.Width, 0f);
            gl.BindTexture(TextureTarget.Texture2D, _blurA.Texture);
            gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            _blurA.Bind(false);
            gl.Uniform2(_gauss.Uniform("uStep"), 0f, sigma / _blurA.Height);
            gl.BindTexture(TextureTarget.Texture2D, _blurB.Texture);
            gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            source = _blurA.Texture;
        }
        output.Restore(gl);
        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _blit.Use();
        gl.Uniform1(_blit.Uniform("uTexture"), 0);
        // Output uv u maps to source (u − 0.5) / scale + 0.5; texture v runs bottom-up, so a downward offset subtracts.
        var inverse = 1 / MathF.Max(scale, 1e-3f);
        gl.Uniform4(_blit.Uniform("uMap"), inverse, inverse, 0.5f - 0.5f * inverse, 0.5f - 0.5f * inverse + offsetY * inverse);
        gl.Uniform1(_blit.Uniform("uAlpha"), alpha);
        gl.BindTexture(TextureTarget.Texture2D, source);
        gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

    public void Dispose()
    {
        Layer.Dispose();
        _blurA.Dispose();
        _blurB.Dispose();
        _blit.Dispose();
        _gauss.Dispose();
        _gl.DeleteVertexArray(_vao);
    }
}
