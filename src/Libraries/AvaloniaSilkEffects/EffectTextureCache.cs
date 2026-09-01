using System.Numerics;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Silk.NET.OpenGL;

namespace AvaloniaSilkEffects;

public sealed class EffectTextureCache : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<TextTextureKey, EffectTexture> _textTextures = [];
    private readonly Dictionary<string, EffectTexture> _vectorTextures = [];
    private readonly List<EffectTexture> _ownedTextures = [];
    private readonly Dictionary<EffectTexture, ulong> _lastUsedFrame = [];
    private ulong _frame;

    public const long DefaultResidentByteLimit = 256L * 1024 * 1024;
    public const int DefaultResidentTextureLimit = 1024;

    internal EffectTextureCache(GL gl) => _gl = gl;

    public int Count => _ownedTextures.Count;
    public long ResidentBytes => _ownedTextures.Sum(EstimatedBytes);
    public long ResidentByteLimit { get; set; } = DefaultResidentByteLimit;
    public int ResidentTextureLimit { get; set; } = DefaultResidentTextureLimit;

    internal void BeginFrame() => _frame++;

    public void Touch(EffectTexture texture)
    {
        if (!texture.IsDisposed && _lastUsedFrame.ContainsKey(texture))
            _lastUsedFrame[texture] = _frame;
    }

    public Vector2 MeasureText(string text, string fontFamily, float fontSize, int fontWeight)
    {
        var style = fontWeight >= 700 ? SKFontStyle.Bold : SKFontStyle.Normal;
        using var matchedTypeface = SKFontManager.Default.MatchFamily(fontFamily, style);
        using var font = new SKFont(matchedTypeface ?? SKTypeface.Default, fontSize);
        using var shaper = new SKShaper(font.Typeface);
        var shaped = shaper.Shape(text, font);
        font.GetFontMetrics(out var metrics);
        return new(shaped.Width, metrics.Descent - metrics.Ascent);
    }

    public EffectTexture GetOrCreateText(
        string text,
        string fontFamily,
        float fontSize,
        int fontWeight,
        EffectColor color,
        float rasterScale = 2)
    {
        rasterScale = Math.Clamp(rasterScale, 1, 4);
        var key = new TextTextureKey(text, fontFamily, fontSize, fontWeight, color, rasterScale);
        if (_textTextures.TryGetValue(key, out var cached))
        {
            Touch(cached);
            return cached;
        }

        var texture = RasterizeText(key);
        _textTextures.Add(key, texture);
        _ownedTextures.Add(texture);
        _lastUsedFrame[texture] = _frame;
        return texture;
    }

    public unsafe EffectTexture CreateRgba(ReadOnlySpan<byte> rgba, int width, int height, Vector2? logicalSize = null)
    {
        if (rgba.Length != width * height * 4)
            throw new ArgumentException("RGBA data length does not match the texture dimensions.", nameof(rgba));

        var handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        fixed (byte* pixels = rgba)
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba8,
                (uint)width,
                (uint)height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pixels);
        }

        var texture = new EffectTexture(_gl, handle, width, height, logicalSize ?? new Vector2(width, height));
        _ownedTextures.Add(texture);
        _lastUsedFrame[texture] = _frame;
        return texture;
    }

    public EffectTexture GetOrCreateVector(
        string cacheKey,
        Vector2 logicalSize,
        float rasterScale,
        Action<SKCanvas> draw)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheKey);
        ArgumentNullException.ThrowIfNull(draw);
        rasterScale = Math.Clamp(rasterScale, 1, 4);
        var key = $"{cacheKey}\u001f{logicalSize.X:R}\u001f{logicalSize.Y:R}\u001f{rasterScale:R}";
        if (_vectorTextures.TryGetValue(key, out var cached))
        {
            Touch(cached);
            return cached;
        }

        var width = Math.Max(1, (int)Math.Ceiling(logicalSize.X * rasterScale));
        var height = Math.Max(1, (int)Math.Ceiling(logicalSize.Y * rasterScale));
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(rasterScale);
        draw(canvas);
        canvas.Flush();

        var bytes = new byte[bitmap.ByteCount];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);
        var texture = CreateRgba(bytes, width, height, logicalSize);
        _vectorTextures.Add(key, texture);
        return texture;
    }

    private unsafe EffectTexture RasterizeText(TextTextureKey key)
    {
        var style = key.FontWeight >= 700
            ? SKFontStyle.Bold
            : key.FontWeight >= 500 ? SKFontStyle.Normal : SKFontStyle.Normal;
        using var matchedTypeface = SKFontManager.Default.MatchFamily(key.FontFamily, style);
        var typeface = matchedTypeface ?? SKTypeface.Default;
        using var font = new SKFont(typeface, key.FontSize * key.RasterScale)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
        };
        using var shaper = new SKShaper(typeface);
        var shaped = shaper.Shape(key.Text, font);
        font.GetFontMetrics(out var metrics);
        const int padding = 12;
        var width = Math.Max(1, (int)Math.Ceiling(shaped.Width) + padding * 2);
        var height = Math.Max(1, (int)Math.Ceiling(metrics.Descent - metrics.Ascent) + padding * 2);

        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(
                (byte)Math.Clamp(key.Color.R * 255, 0, 255),
                (byte)Math.Clamp(key.Color.G * 255, 0, 255),
                (byte)Math.Clamp(key.Color.B * 255, 0, 255),
                (byte)Math.Clamp(key.Color.A * 255, 0, 255)),
        };
        canvas.Clear(SKColors.Transparent);
        canvas.DrawShapedText(shaper, key.Text, padding, padding - metrics.Ascent, SKTextAlign.Left, font, paint);
        canvas.Flush();

        var handle = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, handle);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, bitmap.GetPixels().ToPointer());
        _gl.GenerateMipmap(TextureTarget.Texture2D);

        return new EffectTexture(
            _gl,
            handle,
            width,
            height,
            new Vector2(width / key.RasterScale, height / key.RasterScale));
    }

    internal void Collect(int maximumIdleFrames = 120)
    {
        var minimumFrame = _frame > (ulong)Math.Max(0, maximumIdleFrames)
            ? _frame - (ulong)maximumIdleFrames
            : 0;
        foreach (var texture in _ownedTextures
            .Where(texture => _lastUsedFrame.GetValueOrDefault(texture) < minimumFrame)
            .ToArray())
            Remove(texture);

        while ((_ownedTextures.Count > ResidentTextureLimit || ResidentBytes > ResidentByteLimit)
            && _ownedTextures
                .Where(texture => _lastUsedFrame.GetValueOrDefault(texture) < _frame)
                .OrderBy(texture => _lastUsedFrame.GetValueOrDefault(texture))
                .FirstOrDefault() is { } oldest)
            Remove(oldest);
    }

    private void Remove(EffectTexture texture)
    {
        foreach (var key in _textTextures.Where(pair => ReferenceEquals(pair.Value, texture)).Select(pair => pair.Key).ToArray())
            _textTextures.Remove(key);
        foreach (var key in _vectorTextures.Where(pair => ReferenceEquals(pair.Value, texture)).Select(pair => pair.Key).ToArray())
            _vectorTextures.Remove(key);
        _ownedTextures.Remove(texture);
        _lastUsedFrame.Remove(texture);
        texture.Dispose();
    }

    private static long EstimatedBytes(EffectTexture texture) =>
        (long)texture.Width * texture.Height * 4 * 4 / 3;

    public void Dispose()
    {
        foreach (var texture in _ownedTextures)
            texture.Dispose();
        _ownedTextures.Clear();
        _lastUsedFrame.Clear();
        _textTextures.Clear();
        _vectorTextures.Clear();
    }

    internal void Abandon()
    {
        foreach (var texture in _ownedTextures)
            texture.Abandon();
        _ownedTextures.Clear();
        _lastUsedFrame.Clear();
        _textTextures.Clear();
        _vectorTextures.Clear();
    }
}
