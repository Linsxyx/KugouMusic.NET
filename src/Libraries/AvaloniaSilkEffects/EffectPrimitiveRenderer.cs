using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct EffectVertex(Vector2 Position, Vector2 Uv, Vector4 Color);

public sealed class EffectPrimitiveRenderer : IDisposable
{
    private const string VertexShader = """
        #version 330 core
        layout (location = 0) in vec2 aPosition;
        layout (location = 1) in vec2 aUv;
        layout (location = 2) in vec4 aColor;
        uniform vec2 uViewport;
        out vec2 vUv;
        out vec4 vColor;
        void main() {
            vec2 clip = vec2(aPosition.x * 2.0 / uViewport.x - 1.0,
                             1.0 - aPosition.y * 2.0 / uViewport.y);
            gl_Position = vec4(clip, 0.0, 1.0);
            vUv = aUv;
            vColor = aColor;
        }
        """;

    private const string FragmentShader = """
        #version 330 core
        in vec2 vUv;
        in vec4 vColor;
        uniform sampler2D uTexture;
        out vec4 finalColor;
        void main() {
            finalColor = texture(uTexture, vUv) * vColor;
        }
        """;

    private readonly GL _gl;
    private readonly EffectShaderProgram _shader;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _whiteTexture;
    private readonly List<EffectVertex> _vertices = new(2048);
    private uint _activeTexture;
    private EffectBlendMode _activeBlend;
    private Vector2 _viewport;
    private nuint _bufferCapacityBytes;

    internal int FrameDrawCalls { get; private set; }
    internal int FrameFlushes { get; private set; }
    internal long FrameUploadedBytes { get; private set; }

    internal unsafe EffectPrimitiveRenderer(GL gl)
    {
        _gl = gl;
        _shader = new EffectShaderProgram(gl, VertexShader, FragmentShader, "effects-2d");
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        var stride = (uint)Marshal.SizeOf<EffectVertex>();
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)(4 * sizeof(float)));
        gl.EnableVertexAttribArray(2);

        _whiteTexture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        var white = 0xFFFFFFFFu;
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 1, 1, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, &white);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
    }

    internal void Begin(int width, int height)
    {
        _viewport = new Vector2(width, height);
        _activeTexture = _whiteTexture;
        _activeBlend = EffectBlendMode.Alpha;
        _vertices.Clear();
        FrameDrawCalls = 0;
        FrameFlushes = 0;
        FrameUploadedBytes = 0;
    }

    public void DrawShape(ShapeNode shape)
    {
        var color = WithAlpha(shape.Color, shape.WorldAlpha);
        switch (shape.Shape)
        {
            case EffectShapeKind.Rectangle:
                DrawQuad(shape.WorldTransform, shape.Size, color, shape.BlendMode, _whiteTexture);
                break;
            case EffectShapeKind.Ellipse:
                DrawEllipse(shape.WorldTransform, shape.Size, color, shape.BlendMode);
                break;
            case EffectShapeKind.Line:
                DrawLine(shape.WorldTransform, Vector2.Zero, shape.Size, shape.StrokeWidth, color, shape.BlendMode);
                break;
        }
    }

    public void DrawPolyline(PolylineNode polyline)
    {
        var start = Math.Clamp(polyline.StartPointIndex, 0, polyline.Points.Count - 2);
        var end = Math.Clamp(polyline.EndPointIndex, start + 1, polyline.Points.Count - 1);
        var pointCount = end - start + 1;
        if (pointCount < 2)
            return;

        Select(_whiteTexture, polyline.BlendMode);
        var positions = ArrayPool<Vector2>.Shared.Rent(pointCount);
        var normals = ArrayPool<Vector2>.Shared.Rent(pointCount - 1);
        var left = ArrayPool<Vector2>.Shared.Rent(pointCount);
        var right = ArrayPool<Vector2>.Shared.Rent(pointCount);
        var colors = ArrayPool<Vector4>.Shared.Rent(pointCount);
        try
        {
            for (var index = 0; index < pointCount; index++)
                positions[index] = Vector2.Transform(polyline.Points[start + index], polyline.WorldTransform);
            for (var index = 0; index < pointCount - 1; index++)
            {
                var delta = positions[index + 1] - positions[index];
                normals[index] = delta.LengthSquared() <= float.Epsilon
                    ? (index > 0 ? normals[index - 1] : Vector2.UnitY)
                    : Vector2.Normalize(new Vector2(-delta.Y, delta.X));
            }

            for (var index = 0; index < pointCount; index++)
            {
                var progress = index / (float)(pointCount - 1);
                var halfWidth = MathF.Max(0.01f, Lerp(polyline.TailWidth, polyline.HeadWidth, progress) * 0.5f);
                Vector2 join;
                float miterLength;
                if (index == 0)
                {
                    join = normals[0];
                    miterLength = halfWidth;
                }
                else if (index == pointCount - 1)
                {
                    join = normals[pointCount - 2];
                    miterLength = halfWidth;
                }
                else
                {
                    var sum = normals[index - 1] + normals[index];
                    join = sum.LengthSquared() <= 0.000001f ? normals[index] : Vector2.Normalize(sum);
                    var denominator = MathF.Abs(Vector2.Dot(join, normals[index]));
                    miterLength = MathF.Min(halfWidth / MathF.Max(0.2f, denominator), halfWidth * 2.5f);
                }
                left[index] = positions[index] - join * miterLength;
                right[index] = positions[index] + join * miterLength;
                var alpha = polyline.Color.A * polyline.WorldAlpha * Lerp(polyline.TailAlpha, polyline.HeadAlpha, progress);
                colors[index] = (polyline.Color with { A = Math.Clamp(alpha, 0, 1) }).Premultiplied().ToVector4();
            }

            for (var index = 0; index < pointCount - 1; index++)
            {
                AddTriangle(left[index], left[index + 1], right[index + 1], colors[index], colors[index + 1], colors[index + 1]);
                AddTriangle(left[index], right[index + 1], right[index], colors[index], colors[index + 1], colors[index]);
            }
        }
        finally
        {
            ArrayPool<Vector2>.Shared.Return(positions);
            ArrayPool<Vector2>.Shared.Return(normals);
            ArrayPool<Vector2>.Shared.Return(left);
            ArrayPool<Vector2>.Shared.Return(right);
            ArrayPool<Vector4>.Shared.Return(colors);
        }
    }

    public void DrawPolygon(PolygonNode polygon)
    {
        Select(_whiteTexture, polygon.BlendMode);
        var color = WithAlpha(polygon.Color, polygon.WorldAlpha).Premultiplied().ToVector4();
        var transform = polygon.WorldTransform;
        var indices = polygon.TriangleIndices;
        for (var index = 0; index < indices.Length; index += 3)
            AddTriangle(
                Vector2.Transform(polygon.Points[indices[index]], transform),
                Vector2.Transform(polygon.Points[indices[index + 1]], transform),
                Vector2.Transform(polygon.Points[indices[index + 2]], transform),
                color);
    }

    public void DrawSprite(SpriteNode sprite) => DrawTexture(
        sprite.Texture!,
        sprite.WorldTransform,
        sprite.Size == Vector2.Zero ? sprite.Texture!.LogicalSize : sprite.Size,
        sprite.WorldAlpha,
        sprite.BlendMode,
        sprite.Tint);

    public void DrawTexture(
        EffectTexture texture,
        Matrix3x2 transform,
        Vector2 size,
        float alpha = 1,
        EffectBlendMode blendMode = EffectBlendMode.Alpha,
        EffectColor? tint = null) =>
        DrawQuad(transform, size, WithAlpha(tint ?? EffectColor.White, alpha), blendMode, texture.Handle);

    public void DrawRectangle(
        Matrix3x2 transform,
        Vector2 size,
        EffectColor color,
        EffectBlendMode blendMode = EffectBlendMode.Alpha) =>
        DrawQuad(transform, size, color, blendMode, _whiteTexture);

    public void DrawLine(
        Matrix3x2 transform,
        Vector2 start,
        Vector2 end,
        float width,
        EffectColor color,
        EffectBlendMode blendMode = EffectBlendMode.Alpha)
    {
        Select(_whiteTexture, blendMode);
        var delta = end - start;
        if (delta.LengthSquared() < float.Epsilon)
            return;
        var normal = Vector2.Normalize(new Vector2(-delta.Y, delta.X)) * (width * 0.5f);
        AddQuad(
            Vector2.Transform(start - normal, transform),
            Vector2.Transform(end - normal, transform),
            Vector2.Transform(end + normal, transform),
            Vector2.Transform(start + normal, transform),
            color.Premultiplied().ToVector4());
    }

    public void DrawEllipse(
        Matrix3x2 transform,
        Vector2 size,
        EffectColor color,
        EffectBlendMode blendMode = EffectBlendMode.Alpha,
        int segments = 48)
    {
        Select(_whiteTexture, blendMode);
        segments = Math.Clamp(segments, 12, 128);
        var center = Vector2.Transform(size * 0.5f, transform);
        var packed = color.Premultiplied().ToVector4();
        for (var i = 0; i < segments; i++)
        {
            var a0 = MathF.Tau * i / segments;
            var a1 = MathF.Tau * (i + 1) / segments;
            var p0 = Vector2.Transform(new Vector2(
                size.X * (0.5f + 0.5f * MathF.Cos(a0)),
                size.Y * (0.5f + 0.5f * MathF.Sin(a0))), transform);
            var p1 = Vector2.Transform(new Vector2(
                size.X * (0.5f + 0.5f * MathF.Cos(a1)),
                size.Y * (0.5f + 0.5f * MathF.Sin(a1))), transform);
            AddTriangle(center, p0, p1, packed);
        }
    }

    internal unsafe void Flush()
    {
        if (_vertices.Count == 0)
            return;

        _shader.Use();
        _gl.Uniform2(_shader.Uniform("uViewport"), _viewport.X, _viewport.Y);
        _gl.Uniform1(_shader.Uniform("uTexture"), 0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _activeTexture);
        ApplyBlend(_activeBlend);
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        var data = CollectionsMarshal.AsSpan(_vertices);
        var uploadBytes = (nuint)(data.Length * Marshal.SizeOf<EffectVertex>());
        fixed (EffectVertex* vertices = data)
        {
            if (uploadBytes > _bufferCapacityBytes)
            {
                _bufferCapacityBytes = GrowCapacity(uploadBytes);
                _gl.BufferData(BufferTargetARB.ArrayBuffer, _bufferCapacityBytes, null, BufferUsageARB.StreamDraw);
            }
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, uploadBytes, vertices);
        }
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)data.Length);
        FrameDrawCalls++;
        FrameFlushes++;
        FrameUploadedBytes += (long)uploadBytes;
        _vertices.Clear();
    }

    internal static nuint GrowCapacity(nuint required)
    {
        nuint capacity = 64 * 1024;
        while (capacity < required)
            capacity *= 2;
        return capacity;
    }

    private void DrawQuad(Matrix3x2 transform, Vector2 size, EffectColor color, EffectBlendMode blendMode, uint texture)
    {
        Select(texture, blendMode);
        AddQuad(
            Vector2.Transform(Vector2.Zero, transform),
            Vector2.Transform(new Vector2(size.X, 0), transform),
            Vector2.Transform(size, transform),
            Vector2.Transform(new Vector2(0, size.Y), transform),
            color.Premultiplied().ToVector4());
    }

    private void AddQuad(Vector2 topLeft, Vector2 topRight, Vector2 bottomRight, Vector2 bottomLeft, Vector4 color)
    {
        _vertices.Add(new EffectVertex(topLeft, new Vector2(0, 0), color));
        _vertices.Add(new EffectVertex(topRight, new Vector2(1, 0), color));
        _vertices.Add(new EffectVertex(bottomRight, new Vector2(1, 1), color));
        _vertices.Add(new EffectVertex(topLeft, new Vector2(0, 0), color));
        _vertices.Add(new EffectVertex(bottomRight, new Vector2(1, 1), color));
        _vertices.Add(new EffectVertex(bottomLeft, new Vector2(0, 1), color));
    }

    private void AddTriangle(Vector2 a, Vector2 b, Vector2 c, Vector4 color)
    {
        _vertices.Add(new EffectVertex(a, Vector2.Zero, color));
        _vertices.Add(new EffectVertex(b, Vector2.Zero, color));
        _vertices.Add(new EffectVertex(c, Vector2.Zero, color));
    }

    private void AddTriangle(Vector2 a, Vector2 b, Vector2 c, Vector4 colorA, Vector4 colorB, Vector4 colorC)
    {
        _vertices.Add(new EffectVertex(a, Vector2.Zero, colorA));
        _vertices.Add(new EffectVertex(b, Vector2.Zero, colorB));
        _vertices.Add(new EffectVertex(c, Vector2.Zero, colorC));
    }

    private static float Lerp(float start, float end, float progress) => start + (end - start) * progress;

    private void Select(uint texture, EffectBlendMode blendMode)
    {
        if (_vertices.Count > 0 && (_activeTexture != texture || _activeBlend != blendMode))
            Flush();
        _activeTexture = texture;
        _activeBlend = blendMode;
    }

    private void ApplyBlend(EffectBlendMode mode)
    {
        _gl.Enable(EnableCap.Blend);
        switch (mode)
        {
            case EffectBlendMode.Additive:
                _gl.BlendFunc(BlendingFactor.One, BlendingFactor.One);
                break;
            case EffectBlendMode.Screen:
                _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcColor);
                break;
            case EffectBlendMode.Multiply:
                _gl.BlendFunc(BlendingFactor.DstColor, BlendingFactor.OneMinusSrcAlpha);
                break;
            default:
                _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                break;
        }
    }

    private static EffectColor WithAlpha(EffectColor color, float alpha) =>
        color with { A = color.A * Math.Clamp(alpha, 0, 1) };

    public void Dispose()
    {
        _shader.Dispose();
        _gl.DeleteTexture(_whiteTexture);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
