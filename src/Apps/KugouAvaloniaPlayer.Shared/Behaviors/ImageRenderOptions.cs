using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace KugouAvaloniaPlayer.Behaviors;

public static class ImageRenderOptions
{
    public static readonly AttachedProperty<string?> InterpolationSourceProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, string?>(
            "InterpolationSource",
            typeof(ImageRenderOptions));

    public static string? GetInterpolationSource(AvaloniaObject obj)
    {
        return obj.GetValue(InterpolationSourceProperty);
    }

    public static void SetInterpolationSource(AvaloniaObject obj, string? value)
    {
        obj.SetValue(InterpolationSourceProperty, value);
    }

    static ImageRenderOptions()
    {
        InterpolationSourceProperty.Changed.AddClassHandler<Visual>((visual, args) =>
        {
            var source = args.NewValue as string;
            var mode = source?.StartsWith("avares://", System.StringComparison.OrdinalIgnoreCase) == true
                ? BitmapInterpolationMode.HighQuality
                : BitmapInterpolationMode.LowQuality;

            RenderOptions.SetBitmapInterpolationMode(visual, mode);
        });
    }
}
