using System;
using System.IO;
using SkiaSharp;

namespace KugouAvaloniaPlayer.Services;

internal static class StaticImageBlurProcessor
{
    private const int MaxOutputWidth = 960;

    public static byte[] CreateBlurredPng(
        ReadOnlySpan<byte> encodedSource,
        double blurRadius,
        double displayWidth)
    {
        using var sourceData = SKData.CreateCopy(encodedSource);
        using var source = SKBitmap.Decode(sourceData)
                           ?? throw new InvalidDataException("无法解码播放页背景图片。");

        var outputWidth = Math.Min(source.Width, MaxOutputWidth);
        var outputHeight = Math.Max(1, (int)Math.Round(source.Height * (double)outputWidth / source.Width));
        var imageInfo = new SKImageInfo(outputWidth, outputHeight, SKColorType.Bgra8888, SKAlphaType.Premul);

        using var surface = SKSurface.Create(imageInfo)
                            ?? throw new InvalidOperationException("无法创建播放页背景模糊画布。");
        using var paint = new SKPaint();
        paint.IsAntialias = true;

        if (blurRadius > 0)
        {
            var effectiveDisplayWidth = displayWidth > 0 ? displayWidth : outputWidth;
            var radiusInOutputPixels = blurRadius * outputWidth / effectiveDisplayWidth;
            var sigma = (float)(radiusInOutputPixels * 0.57735 + 0.5);
            paint.ImageFilter = SKImageFilter.CreateBlur(sigma, sigma);
        }

        var destination = SKRect.Create(outputWidth, outputHeight);
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawBitmap(source, destination, paint);
        surface.Canvas.Flush();

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
                            ?? throw new InvalidOperationException("无法编码播放页背景模糊图片。");
        return encoded.ToArray();
    }
}
