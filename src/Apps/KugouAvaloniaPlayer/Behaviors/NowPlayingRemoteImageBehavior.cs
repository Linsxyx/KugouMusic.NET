using System;
using System.Runtime.CompilerServices;
using AsyncImageLoader;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace KugouAvaloniaPlayer.Behaviors;

public static class NowPlayingRemoteImageBehavior
{
    private static readonly ConditionalWeakTable<Image, ImageState> States = new();

    public static readonly AttachedProperty<string?> SourceProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>(
            "Source",
            typeof(NowPlayingRemoteImageBehavior));

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Image, bool>(
            "IsEnabled",
            typeof(NowPlayingRemoteImageBehavior),
            defaultValue: true);

    static NowPlayingRemoteImageBehavior()
    {
        SourceProperty.Changed.AddClassHandler<Image>(OnLoadingPropertyChanged);
        IsEnabledProperty.Changed.AddClassHandler<Image>(OnLoadingPropertyChanged);
        Image.SourceProperty.Changed.AddClassHandler<Image>(OnImageSourceChanged);
    }

    public static string? GetSource(AvaloniaObject element) => element.GetValue(SourceProperty);

    public static void SetSource(AvaloniaObject element, string? value) =>
        element.SetValue(SourceProperty, value);

    public static bool GetIsEnabled(AvaloniaObject element) => element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(AvaloniaObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnLoadingPropertyChanged(Image image, AvaloniaPropertyChangedEventArgs args)
    {
        States.GetValue(image, static owner => new ImageState(owner)).Reload();
    }

    private static void OnImageSourceChanged(Image image, AvaloniaPropertyChangedEventArgs args)
    {
        if (States.TryGetValue(image, out var state))
            state.TrackAppliedBitmap(args.GetNewValue<Avalonia.Media.IImage?>() as Bitmap);
    }

    private sealed class ImageState
    {
        private readonly Image _image;
        private Bitmap? _ownedBitmap;
        private string? _appliedSource;
        private bool _isAttached;

        public ImageState(Image image)
        {
            _image = image;
            _isAttached = image.IsAttachedToVisualTree();
            image.AttachedToVisualTree += OnAttachedToVisualTree;
            image.DetachedFromVisualTree += OnDetachedFromVisualTree;
        }

        public void Reload()
        {
            var source = GetSource(_image);
            if (!_isAttached || !GetIsEnabled(_image) || string.IsNullOrWhiteSpace(source))
            {
                Release();
                return;
            }

            if (string.Equals(_appliedSource, source, StringComparison.Ordinal))
                return;

            Release();
            _appliedSource = source;
            ImageLoader.SetSource(_image, source);
        }

        public void TrackAppliedBitmap(Bitmap? bitmap)
        {
            if (_appliedSource == null ||
                bitmap == null ||
                LocalArtworkImageBehavior.OwnsBitmap(_image, bitmap) ||
                ReferenceEquals(_ownedBitmap, bitmap))
                return;

            var previous = _ownedBitmap;
            _ownedBitmap = bitmap;
            previous?.Dispose();
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
        {
            _isAttached = true;
            Reload();
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
        {
            _isAttached = false;
            Release();
        }

        private void Release()
        {
            if (_appliedSource == null && _ownedBitmap == null)
                return;

            _appliedSource = null;
            var bitmap = _ownedBitmap;
            _ownedBitmap = null;

            ImageLoader.SetSource(_image, null);
            if (bitmap == null) return;
            if (ReferenceEquals(_image.Source, bitmap))
                _image.Source = null;

            bitmap.Dispose();
        }
    }
}
