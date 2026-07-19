using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ATL;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.Converters;

namespace KugouAvaloniaPlayer.Behaviors;

public static class LocalArtworkImageBehavior
{
    private const string DefaultSongCover = "avares://KugouAvaloniaPlayer/Assets/default_song.png";
    private const int DefaultDecodePixelWidth = 96;

    private static readonly ConditionalWeakTable<Image, ImageState> States = new();
    private static readonly Lazy<Bitmap> SharedDefaultBitmap = new(CreateDefaultBitmap);

    public static readonly AttachedProperty<string?> SourceProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Source", typeof(LocalArtworkImageBehavior));

    public static readonly AttachedProperty<int> DecodePixelWidthProperty =
        AvaloniaProperty.RegisterAttached<Image, int>(
            "DecodePixelWidth",
            typeof(LocalArtworkImageBehavior),
            defaultValue: DefaultDecodePixelWidth);

    public static readonly AttachedProperty<BitmapInterpolationMode> DecodeInterpolationModeProperty =
        AvaloniaProperty.RegisterAttached<Image, BitmapInterpolationMode>(
            "DecodeInterpolationMode",
            typeof(LocalArtworkImageBehavior),
            defaultValue: BitmapInterpolationMode.LowQuality);

    public static readonly AttachedProperty<bool> UseDefaultFallbackProperty =
        AvaloniaProperty.RegisterAttached<Image, bool>(
            "UseDefaultFallback",
            typeof(LocalArtworkImageBehavior),
            defaultValue: true);

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Image, bool>(
            "IsEnabled",
            typeof(LocalArtworkImageBehavior),
            defaultValue: true);

    static LocalArtworkImageBehavior()
    {
        SourceProperty.Changed.AddClassHandler<Image>(OnLoadingPropertyChanged);
        DecodePixelWidthProperty.Changed.AddClassHandler<Image>(OnLoadingPropertyChanged);
        DecodeInterpolationModeProperty.Changed.AddClassHandler<Image>(OnLoadingPropertyChanged);
        UseDefaultFallbackProperty.Changed.AddClassHandler<Image>(OnLoadingPropertyChanged);
        IsEnabledProperty.Changed.AddClassHandler<Image>(OnLoadingPropertyChanged);
    }

    public static string? GetSource(AvaloniaObject element) => element.GetValue(SourceProperty);

    public static void SetSource(AvaloniaObject element, string? value) => element.SetValue(SourceProperty, value);

    public static int GetDecodePixelWidth(AvaloniaObject element) => element.GetValue(DecodePixelWidthProperty);

    public static void SetDecodePixelWidth(AvaloniaObject element, int value) =>
        element.SetValue(DecodePixelWidthProperty, value);

    public static BitmapInterpolationMode GetDecodeInterpolationMode(AvaloniaObject element) =>
        element.GetValue(DecodeInterpolationModeProperty);

    public static void SetDecodeInterpolationMode(AvaloniaObject element, BitmapInterpolationMode value) =>
        element.SetValue(DecodeInterpolationModeProperty, value);

    public static bool GetUseDefaultFallback(AvaloniaObject element) =>
        element.GetValue(UseDefaultFallbackProperty);

    public static void SetUseDefaultFallback(AvaloniaObject element, bool value) =>
        element.SetValue(UseDefaultFallbackProperty, value);

    public static bool GetIsEnabled(AvaloniaObject element) => element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(AvaloniaObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnLoadingPropertyChanged(Image image, AvaloniaPropertyChangedEventArgs args)
    {
        States.GetValue(image, static owner => new ImageState(owner)).Reload();
    }

    private static Bitmap CreateDefaultBitmap()
    {
        using var stream = AssetLoader.Open(new Uri(DefaultSongCover));
        return new Bitmap(stream);
    }

    private static Bitmap? DecodeLocalBitmap(
        string source,
        int decodePixelWidth,
        BitmapInterpolationMode interpolationMode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (LocalImageSourceHelper.TryGetEmbeddedCoverFilePath(source, out var embeddedTrackPath))
        {
            var track = new Track(embeddedTrackPath!);
            var picture = track.EmbeddedPictures.Count > 0 ? track.EmbeddedPictures[0] : null;
            var pictureData = picture?.PictureData;
            if (pictureData is not { Length: > 0 })
                return null;

            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(pictureData, writable: false);
            return Bitmap.DecodeToWidth(stream, decodePixelWidth, interpolationMode);
        }

        var path = LocalImageSourceHelper.GetLocalFilePath(source);
        if (path is null)
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        using var fileStream = File.OpenRead(path);
        return Bitmap.DecodeToWidth(fileStream, decodePixelWidth, interpolationMode);
    }

    private sealed class ImageState
    {
        private readonly Image _image;
        private Bitmap? _ownedBitmap;
        private CancellationTokenSource? _loadCancellation;
        private int _loadVersion;
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
            var version = unchecked(++_loadVersion);
            CancelPendingLoad();
            ReleaseOwnedBitmap();

            if (!GetIsEnabled(_image))
                return;

            var source = GetSource(_image);
            if (string.IsNullOrWhiteSpace(source))
            {
                QueueDefaultFallback(version);
                return;
            }

            var isEmbeddedCover = LocalImageSourceHelper.TryGetEmbeddedCoverFilePath(source, out _);
            var isLocalFile = LocalImageSourceHelper.GetLocalFilePath(source) is not null;
            if (!isEmbeddedCover && !isLocalFile)
                return;

            if (!_isAttached)
                return;

            var decodePixelWidth = Math.Max(1, GetDecodePixelWidth(_image));
            var interpolationMode = GetDecodeInterpolationMode(_image);
            var cancellation = new CancellationTokenSource();
            _loadCancellation = cancellation;
            _ = LoadAndApplyAsync(source, decodePixelWidth, interpolationMode, version, cancellation);
        }

        private async Task LoadAndApplyAsync(
            string source,
            int decodePixelWidth,
            BitmapInterpolationMode interpolationMode,
            int version,
            CancellationTokenSource cancellation)
        {
            Bitmap? bitmap = null;
            try
            {
                bitmap = await Task.Run(
                        () => DecodeLocalBitmap(
                            source,
                            decodePixelWidth,
                            interpolationMode,
                            cancellation.Token),
                        cancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A newer source or a detached control owns the next state.
            }
            catch
            {
                // Invalid or unreadable artwork falls back to the shared default image.
            }

            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var isCanceled = cancellation.IsCancellationRequested;
                    if (ReferenceEquals(_loadCancellation, cancellation))
                        _loadCancellation = null;

                    cancellation.Dispose();

                    if (version != _loadVersion || !_isAttached || isCanceled)
                    {
                        bitmap?.Dispose();
                        bitmap = null;
                        return;
                    }

                    if (bitmap is null)
                    {
                        QueueDefaultFallback(version);
                        return;
                    }

                    ReplaceOwnedBitmap(bitmap);
                    bitmap = null;
                });
            }
            catch
            {
                cancellation.Dispose();
                bitmap?.Dispose();
            }
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
        {
            _isAttached = true;
            Reload();
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
        {
            _isAttached = false;
            unchecked
            {
                _loadVersion++;
            }

            CancelPendingLoad();
            ReleaseOwnedBitmap();
        }

        private void ReplaceOwnedBitmap(Bitmap bitmap)
        {
            var previous = _ownedBitmap;
            _ownedBitmap = bitmap;
            _image.Source = bitmap;
            previous?.Dispose();
        }

        private void ReleaseOwnedBitmap()
        {
            var previous = _ownedBitmap;
            _ownedBitmap = null;
            if (previous is null)
                return;

            if (ReferenceEquals(_image.Source, previous))
                _image.Source = null;

            previous.Dispose();
        }

        private void CancelPendingLoad()
        {
            var cancellation = _loadCancellation;
            _loadCancellation = null;
            if (cancellation is null)
                return;

            cancellation.Cancel();
        }

        private void QueueDefaultFallback(int version)
        {
            if (!GetIsEnabled(_image) || !GetUseDefaultFallback(_image) || !_isAttached)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                if (version == _loadVersion &&
                    GetIsEnabled(_image) &&
                    _isAttached &&
                    _ownedBitmap is null)
                    _image.Source = SharedDefaultBitmap.Value;
            }, DispatcherPriority.Background);
        }
    }
}
