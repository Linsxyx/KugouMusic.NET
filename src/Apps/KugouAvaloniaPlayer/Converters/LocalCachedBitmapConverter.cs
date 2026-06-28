using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using ATL;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace KugouAvaloniaPlayer.Converters;

public sealed class LocalCachedBitmapConverter : IValueConverter
{
    private const int DefaultDecodeWidth = 96;
    private const string DefaultSongCover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";
    private static readonly ConcurrentDictionary<string, WeakReference<Bitmap>> Cache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<int, Lazy<Bitmap>> DefaultBitmapCache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var decodeWidth = ResolveDecodeWidth(parameter);
        var source = value as string;
        if (string.IsNullOrWhiteSpace(source))
            return GetDefaultBitmap(decodeWidth);

        var cacheKey = BuildCacheKey(source, decodeWidth);

        if (Cache.TryGetValue(cacheKey, out var weakReference) &&
            weakReference.TryGetTarget(out var cached))
        {
            return cached;
        }

        if (LocalImageSourceHelper.TryGetEmbeddedCoverFilePath(source, out var embeddedTrackPath))
        {
            var bitmap = TryDecodeEmbeddedCoverBitmap(embeddedTrackPath!, decodeWidth);
            if (bitmap != null)
                Cache[cacheKey] = new WeakReference<Bitmap>(bitmap);
            else
                Cache.TryRemove(cacheKey, out _);

            return bitmap ?? GetDefaultBitmap(decodeWidth);
        }

        var path = LocalImageSourceHelper.GetLocalFilePath(source);
        if (path == null)
            return GetDefaultBitmap(decodeWidth);

        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.LowQuality);
            Cache[cacheKey] = new WeakReference<Bitmap>(bitmap);
            return bitmap;
        }
        catch
        {
            Cache.TryRemove(cacheKey, out _);
            return GetDefaultBitmap(decodeWidth);
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static Bitmap? TryDecodeEmbeddedCoverBitmap(string trackPath, int decodeWidth)
    {
        try
        {
            var track = new Track(trackPath);
            var picture = track.EmbeddedPictures.Count > 0 ? track.EmbeddedPictures[0] : null;
            if (picture?.PictureData == null || picture.PictureData.Length == 0)
                return null;

            using var stream = new MemoryStream(picture.PictureData, writable: false);
            return Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.LowQuality);
        }
        catch
        {
            return null;
        }
    }

    private static int ResolveDecodeWidth(object? parameter)
    {
        if (parameter is int width && width > 0)
            return width;

        if (parameter is string s &&
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0)
        {
            return parsed;
        }

        return DefaultDecodeWidth;
    }

    private static string BuildCacheKey(string source, int decodeWidth)
    {
        return source + "|w=" + decodeWidth.ToString(CultureInfo.InvariantCulture);
    }

    private static Bitmap GetDefaultBitmap(int decodeWidth)
    {
        return DefaultBitmapCache.GetOrAdd(decodeWidth, width => new Lazy<Bitmap>(() =>
        {
            using var stream = AssetLoader.Open(new Uri(DefaultSongCover));
            return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.LowQuality);
        })).Value;
    }
}
