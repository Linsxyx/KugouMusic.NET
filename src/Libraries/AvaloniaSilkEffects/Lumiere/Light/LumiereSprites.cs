using System.Numerics;
using Silk.NET.OpenGL;
using SkiaSharp;

namespace AvaloniaSilkEffects.Lumiere.Light;

/// <summary>
/// Folia lumiere/light/sprites.ts: procedural optical sprites, white and premultiplied, colored by tint —
/// a soft dot (motes, glyph halos), a four-point star, a bokeh disc and a horizontal streak. Owned here
/// rather than by <see cref="EffectTextureCache"/>, whose idle sweep would evict them between uses.
/// </summary>
internal sealed class LumiereSprites : IDisposable
{
    public LumiereSprites(GL gl)
    {
        Dot = Upload(gl, DrawDot(128));
        Star = Upload(gl, DrawStar(256));
        Bokeh = Upload(gl, DrawBokeh(128));
        Streak = Upload(gl, DrawStreak(512, 32));
    }

    public EffectTexture Dot { get; }
    public EffectTexture Star { get; }
    public EffectTexture Bokeh { get; }
    public EffectTexture Streak { get; }

    private static SKColor White(double alpha) => new(255, 255, 255, (byte)Math.Round(alpha * 255));

    private static SKShader Radial(float cx, float cy, float radius, params (float Stop, double Alpha)[] stops) =>
        SKShader.CreateRadialGradient(new SKPoint(cx, cy), radius,
            stops.Select(stop => White(stop.Alpha)).ToArray(), stops.Select(stop => stop.Stop).ToArray(), SKShaderTileMode.Clamp);

    internal static SKBitmap DrawDot(int size)
    {
        var bitmap = Create(size, size, out var canvas);
        using (canvas)
        {
            var r = size / 2f;
            using var paint = new SKPaint { Shader = Radial(r, r, r, (0, 1), (0.18f, 0.72), (0.45f, 0.2), (1, 0)) };
            canvas.DrawRect(0, 0, size, size, paint);
        }
        return bitmap;
    }

    internal static SKBitmap DrawStar(int size)
    {
        var bitmap = Create(size, size, out var canvas);
        using (canvas)
        {
            var r = size / 2f;
            using (var halo = new SKPaint { Shader = Radial(r, r, r * 0.5f, (0, 1), (0.12f, 0.8), (0.4f, 0.14), (1, 0)) })
                canvas.DrawRect(0, 0, size, size, halo);
            // Four rays: thin diamonds, bright in the middle and pointed at the tips ('lighter' = additive).
            void Ray(float angle, float length, float thickness, double alpha)
            {
                canvas.Save();
                canvas.Translate(r, r);
                canvas.RotateRadians(angle);
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    BlendMode = SKBlendMode.Plus,
                    Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(length, 0),
                        [White(alpha), White(0)], SKShaderTileMode.Clamp),
                };
                using var path = new SKPath();
                path.MoveTo(0, -thickness);
                path.LineTo(length, 0);
                path.LineTo(0, thickness);
                path.Close();
                canvas.DrawPath(path, paint);
                canvas.Restore();
            }
            for (var i = 0; i < 4; i++) Ray(MathF.PI / 2 * i, r * 0.98f, size * 0.018f, 0.95);
            for (var i = 0; i < 4; i++) Ray(MathF.PI / 2 * i + MathF.PI / 4, r * 0.42f, size * 0.01f, 0.45);
        }
        return bitmap;
    }

    internal static SKBitmap DrawBokeh(int size)
    {
        var bitmap = Create(size, size, out var canvas);
        using (canvas)
        {
            var r = size / 2f;
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Shader = Radial(r, r, r * 0.96f, (0, 0.35), (0.78f, 0.42), (0.9f, 0.75), (1, 0)),
            };
            canvas.DrawCircle(r, r, r * 0.96f, paint);
        }
        return bitmap;
    }

    internal static SKBitmap DrawStreak(int width, int height)
    {
        var bitmap = Create(width, height, out var canvas);
        using (canvas)
        {
            using (var horizontal = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, 0),
                    [White(0), White(1), White(0)], [0, 0.5f, 1], SKShaderTileMode.Clamp),
            })
                canvas.DrawRect(0, 0, width, height, horizontal);
            using var vertical = new SKPaint
            {
                BlendMode = SKBlendMode.DstIn,
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, height),
                    [White(0), White(1), White(0)], [0, 0.5f, 1], SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(0, 0, width, height, vertical);
        }
        return bitmap;
    }

    private static SKBitmap Create(int width, int height, out SKCanvas canvas)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        return bitmap;
    }

    private static unsafe EffectTexture Upload(GL gl, SKBitmap bitmap)
    {
        using (bitmap)
        {
            var handle = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, handle);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)bitmap.Width, (uint)bitmap.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, (void*)bitmap.GetPixels());
            return new EffectTexture(gl, handle, bitmap.Width, bitmap.Height, new Vector2(bitmap.Width, bitmap.Height));
        }
    }

    public void Dispose()
    {
        Dot.Dispose();
        Star.Dispose();
        Bokeh.Dispose();
        Streak.Dispose();
    }
}
