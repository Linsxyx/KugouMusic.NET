using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;
using Silk.NET.OpenGL;
using ZLinq;

namespace AvaloniaSilkEffects;

public sealed class EffectTextureCache : IDisposable
{
    private const int CollectIntervalFrames = 32;

    private readonly GL _gl;
    private readonly Dictionary<TextTextureKey, EffectTexture> _textTextures = [];
    private readonly Dictionary<VectorTextureKey, EffectTexture> _vectorTextures = [];
    private readonly List<EffectTexture> _ownedTextures = [];
    private readonly List<EffectTexture> _sweepBuffer = [];
    private readonly Dictionary<EffectTexture, ulong> _lastUsedFrame = [];
    private ulong _frame;
    private ulong _lastCollectFrame;
    private long _residentBytes;

    public const long DefaultResidentByteLimit = 256L * 1024 * 1024;
    public const int DefaultResidentTextureLimit = 1024;

    // 常见跨平台中文字体候选列表（优先匹配）
    private static readonly string[] FallbackFontFamilies =
    [
        "Noto Sans CJK SC",
        "Source Han Sans SC",
        "Source Han Sans CN",
        "WenQuanYi Micro Hei",
        "WenQuanYi Zen Hei",
        "Microsoft YaHei",
        "PingFang SC",
        "SimHei",
        "Droid Sans Fallback"
    ];

    internal EffectTextureCache(GL gl) => _gl = gl;

    public int Count => _ownedTextures.Count;
    public long ResidentBytes => _residentBytes;
    public long ResidentByteLimit { get; set; } = DefaultResidentByteLimit;
    public int ResidentTextureLimit { get; set; } = DefaultResidentTextureLimit;

    internal void BeginFrame() => _frame++;

    public void Touch(EffectTexture texture)
    {
        if (texture.IsDisposed)
            return;
        ref var lastUsed = ref CollectionsMarshal.GetValueRefOrNullRef(_lastUsedFrame, texture);
        if (!Unsafe.IsNullRef(ref lastUsed))
            lastUsed = _frame;
    }

    /// <summary>
    /// 获取能够支持当前文本的 Typeface（包含 Linux 中文 Fallback）
    /// </summary>
    private static SKTypeface ResolveTypeface(string? fontFamily, SKFontStyle style, string? sampleText = null)
    {
        // 1. 尝试匹配用户指定的 fontFamily
        if (!string.IsNullOrWhiteSpace(fontFamily))
        {
            var matched = SKFontManager.Default.MatchFamily(fontFamily, style);
            if (matched != null)
            {
                // 如果没有提供采样文本，或该字体包含文本中的首个非空字符，则直接采用
                if (string.IsNullOrEmpty(sampleText) || ContainsGlyph(matched, sampleText))
                    return matched;
            }
        }

        // 2. 如果文本中有中文等特殊字符，优先使用 MatchCharacter 获取支持该字符的系统字体
        if (!string.IsNullOrEmpty(sampleText))
        {
            foreach (var ch in sampleText)
            {
                if (!char.IsWhiteSpace(ch) && ch > 127)
                {
                    var charMatched = SKFontManager.Default.MatchCharacter(ch);
                    if (charMatched != null)
                        return charMatched;
                    break;
                }
            }
        }

        // 3. 尝试常用的中文字体名
        foreach (var fallbackName in FallbackFontFamilies)
        {
            var fallback = SKFontManager.Default.MatchFamily(fallbackName, style);
            if (fallback != null)
                return fallback;
        }

        // 4. 最后降级到系统默认字体
        return SKTypeface.Default;
    }

    private static bool ContainsGlyph(SKTypeface typeface, string text)
    {
        foreach (var ch in text)
        {
            if (ch > 127 && !char.IsWhiteSpace(ch))
            {
                return typeface.ContainsGlyph(ch);
            }
        }
        return true;
    }

    public static Vector2 MeasureText(string text, string fontFamily, float fontSize, int fontWeight)
    {
        if (string.IsNullOrEmpty(text))
            return Vector2.Zero;

        var style = fontWeight >= 700 ? SKFontStyle.Bold : SKFontStyle.Normal;
        using var typeface = ResolveTypeface(fontFamily, style, text);
        using var font = new SKFont(typeface, fontSize);

        var width = font.MeasureText(text);
        font.GetFontMetrics(out var metrics);
        return new Vector2(width, metrics.Descent - metrics.Ascent);
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
        _residentBytes += EstimatedBytes(texture);
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
        _residentBytes += EstimatedBytes(texture);
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
        var key = new VectorTextureKey(cacheKey, logicalSize, rasterScale);
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
        var style = key.FontWeight >= 700 ? SKFontStyle.Bold : SKFontStyle.Normal;
        using var typeface = ResolveTypeface(key.FontFamily, style, key.Text);
        using var font = new SKFont(typeface, key.FontSize * key.RasterScale)
        {
            Edging = SKFontEdging.Antialias,
            Subpixel = true,
        };

        var textWidth = font.MeasureText(key.Text);
        font.GetFontMetrics(out var metrics);
        const int padding = 12;
        var width = Math.Max(1, (int)Math.Ceiling(textWidth) + padding * 2);
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

        // 基线 y 坐标计算
        var y = padding - metrics.Ascent;
        canvas.DrawText(key.Text, padding, y, font, paint);
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
        var pressureExceeded = _ownedTextures.Count > ResidentTextureLimit || _residentBytes > ResidentByteLimit;
        if (!pressureExceeded && _frame - _lastCollectFrame < CollectIntervalFrames)
            return;
        _lastCollectFrame = _frame;

        var minimumFrame = _frame > (ulong)Math.Max(0, maximumIdleFrames)
            ? _frame - (ulong)maximumIdleFrames
            : 0;
        _sweepBuffer.Clear();
        foreach (var texture in _ownedTextures)
            if (_lastUsedFrame.GetValueOrDefault(texture) < minimumFrame)
                _sweepBuffer.Add(texture);
        foreach (var texture in _sweepBuffer)
            Remove(texture);

        while (_ownedTextures.Count > ResidentTextureLimit || _residentBytes > ResidentByteLimit)
        {
            EffectTexture? oldest = null;
            var oldestFrame = ulong.MaxValue;
            foreach (var texture in _ownedTextures)
            {
                var used = _lastUsedFrame.GetValueOrDefault(texture);
                if (used < _frame && used < oldestFrame)
                {
                    oldestFrame = used;
                    oldest = texture;
                }
            }
            if (oldest is null)
                break;
            Remove(oldest);
        }
    }

    private void Remove(EffectTexture texture)
    {
        foreach (var key in _textTextures.AsValueEnumerable().Where(pair => ReferenceEquals(pair.Value, texture)).Select(pair => pair.Key).ToArray())
            _textTextures.Remove(key);
        foreach (var key in _vectorTextures.AsValueEnumerable().Where(pair => ReferenceEquals(pair.Value, texture)).Select(pair => pair.Key).ToArray())
            _vectorTextures.Remove(key);
        _ownedTextures.Remove(texture);
        _residentBytes -= EstimatedBytes(texture);
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
        _sweepBuffer.Clear();
        _residentBytes = 0;
    }

    internal void Abandon()
    {
        foreach (var texture in _ownedTextures)
            texture.Abandon();
        _ownedTextures.Clear();
        _lastUsedFrame.Clear();
        _textTextures.Clear();
        _vectorTextures.Clear();
        _sweepBuffer.Clear();
        _residentBytes = 0;
    }

    private readonly struct VectorTextureKey(string cacheKey, Vector2 logicalSize, float rasterScale) : IEquatable<VectorTextureKey>
    {
        public string CacheKey { get; } = cacheKey;
        public Vector2 LogicalSize { get; } = logicalSize;
        public float RasterScale { get; } = rasterScale;

        public bool Equals(VectorTextureKey other) =>
            CacheKey == other.CacheKey &&
            LogicalSize.X.Equals(other.LogicalSize.X) &&
            LogicalSize.Y.Equals(other.LogicalSize.Y) &&
            RasterScale.Equals(other.RasterScale);

        public override bool Equals(object? obj) => obj is VectorTextureKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(CacheKey, LogicalSize.X, LogicalSize.Y, RasterScale);
    }
}