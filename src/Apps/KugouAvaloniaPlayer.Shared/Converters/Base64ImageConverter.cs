using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace KugouAvaloniaPlayer.Converters;

public class Base64ImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string base64String && !string.IsNullOrWhiteSpace(base64String))
            try
            {
                var commaIndex = base64String.IndexOf(',');
                if (commaIndex >= 0) base64String = base64String.Substring(commaIndex + 1);

                var imageBytes = System.Convert.FromBase64String(base64String);
                using var stream = new MemoryStream(imageBytes);
                return new Bitmap(stream);
            }
            catch
            {
                return null;
            }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string base64String ? System.Convert.FromBase64String(base64String) : null;
    }
}