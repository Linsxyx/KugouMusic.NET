using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using AvaloniaSilkEffects;
using AvaloniaSilkEffects.Backgrounds;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Controls;

// Shared shell layer for Fume and Sonnet, not part of either foreground's filters.
public sealed class LatentBackgroundControl : SilkEffectControl
{
    public static readonly StyledProperty<PlayerViewModel?> PlayerProperty =
        AvaloniaProperty.Register<LatentBackgroundControl,PlayerViewModel?>(nameof(Player));
    public static readonly StyledProperty<IImage?> CoverProperty =
        AvaloniaProperty.Register<LatentBackgroundControl,IImage?>(nameof(Cover));
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<LatentBackgroundControl,bool>(nameof(IsActive));
    public static readonly StyledProperty<bool> IsSonnetProperty =
        AvaloniaProperty.Register<LatentBackgroundControl,bool>(nameof(IsSonnet));
    public PlayerViewModel? Player { get => GetValue(PlayerProperty); set => SetValue(PlayerProperty,value); }
    public IImage? Cover { get => GetValue(CoverProperty); set => SetValue(CoverProperty,value); }
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty,value); }
    public bool IsSonnet { get => GetValue(IsSonnetProperty); set => SetValue(IsSonnetProperty,value); }
    private readonly LatentBackgroundScene _background = new();
    private PlayerViewModel? _subscribed;
    private IReadOnlyList<EffectColor> _coverColors = [];
    private int _generation;
    private bool _attached;

    public LatentBackgroundControl() { TargetFrameRate = 60; IsHitTestVisible = false; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        RefreshSubscription();
        ExtractCover();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _generation++;
        RefreshSubscription();
        base.OnDetachedFromVisualTree(e);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlayerProperty || change.Property == IsActiveProperty)
            RefreshSubscription();
        if (change.Property == CoverProperty) ExtractCover();
        if (change.Property == IsSonnetProperty) UpdatePalette();
    }
    private void RefreshSubscription()
    {
        if (_subscribed != null)
        {
            _subscribed.VisualizerUpdated -= UpdateAudio;
            _subscribed.PropertyChanged -= OnPlayerChanged;
        }
        _subscribed = _attached && IsActive ? Player : null;
        if (_subscribed != null)
        {
            _subscribed.VisualizerUpdated += UpdateAudio;
            _subscribed.PropertyChanged += OnPlayerChanged;
        }
        Scene = _attached && IsActive ? _background : null;
        IsPaused = !_attached || !IsActive;
        UpdatePalette();
        UpdateAudio();
    }
    private void OnPlayerChanged(object? sender,PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.IsPlayingAudio)) UpdateAudio();
    }
    private void UpdateAudio()
    {
        var player = _subscribed;
        var bars = player?.NowPlayingVisualizerBars;
        float Band(int start,int end)
        {
            if (bars == null || bars.Length == 0) return 0;
            var sum = 0f;
            var count = 0;
            for (var i=start;i<Math.Min(end,bars.Length);i++)
            {
                sum += (float)Math.Clamp((bars[i].Height-6)/170,0,1);
                count++;
            }
            return count == 0 ? 0 : sum/count;
        }
        var n = bars?.Length ?? 0;
        _background.Audio = new LatentAudio(Band(0,n),Band(0,n/5),Band(n/5,n*2/5),
            Band(n*2/5,n*3/5),Band(n*3/5,n*4/5),Band(n*4/5,n),player?.IsPlayingAudio != true);
    }
    private void UpdatePalette()
    {
        var theme = IsSonnet ? LatentPalette.Midnight :
            new LatentPalette(new(.031f,.047f,.086f),new(242/255f,235/255f,221/255f),
                new(98/255f,126/255f,145/255f),new(214/255f,169/255f,31/255f),[]);
        _background.Palette = theme with { Cover = _coverColors, UseCoverColorsOnly = true };
    }
    private async void ExtractCover()
    {
        var generation = ++_generation;
        _coverColors = [];
        UpdatePalette();
        if (!_attached || Cover is not Bitmap bitmap) return;
        try
        {
            // Snapshot the loader-owned bitmap on the UI thread, never retain/dispose it.
            using var stream = new MemoryStream();
            bitmap.Save(stream,PngBitmapEncoderOptions.Default);
            var encoded = stream.ToArray();
            var colors = await Task.Run(() => LatentCoverPalette.ExtractEncoded(encoded));
            if (_attached && generation == _generation)
            {
                _coverColors = colors;
                UpdatePalette();
                RenderOnce();
            }
        }
        catch
        {
            // Missing or disposed artwork uses the original theme-color fallback.
            if (_attached && generation == _generation) UpdatePalette();
        }
    }
}
