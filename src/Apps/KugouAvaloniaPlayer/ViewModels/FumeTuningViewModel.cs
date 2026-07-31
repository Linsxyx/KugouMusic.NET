using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;

namespace KugouAvaloniaPlayer.ViewModels;

public sealed partial class FumeTuningViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _saveTimer;
    private readonly FumeThemeSettings _settings;
    private bool _hasPendingSave;
    private bool _suppressSave = true;

    public FumeTuningViewModel()
    {
        _settings = SettingsManager.Settings.FumeTheme ??= new FumeThemeSettings();
        Normalize(_settings);

        _saveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _saveTimer.Tick += OnSaveTimerTick;

        BackgroundObjectOpacity = _settings.BackgroundObjectOpacity;
        TextHoldRatio = _settings.TextHoldRatio;
        CameraTrackingMode = _settings.CameraTrackingMode;
        CameraSpeed = _settings.CameraSpeed;
        GlowIntensity = _settings.GlowIntensity;
        HeroScale = _settings.HeroScale;
        _suppressSave = false;
    }

    [ObservableProperty]
    public partial double BackgroundObjectOpacity { get; set; } = 0.5;

    [ObservableProperty]
    public partial double TextHoldRatio { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSteppedCamera))]
    [NotifyPropertyChangedFor(nameof(IsSmoothCamera))]
    public partial FumeCameraTrackingMode CameraTrackingMode { get; set; } =
        FumeCameraTrackingMode.Smooth;

    [ObservableProperty]
    public partial double CameraSpeed { get; set; } = 1;

    [ObservableProperty]
    public partial double GlowIntensity { get; set; } = 1;

    [ObservableProperty]
    public partial double HeroScale { get; set; } = 1;

    public bool IsSteppedCamera => CameraTrackingMode == FumeCameraTrackingMode.Stepped;

    public bool IsSmoothCamera => CameraTrackingMode == FumeCameraTrackingMode.Smooth;

    [RelayCommand]
    private void UseSteppedCamera()
    {
        CameraTrackingMode = FumeCameraTrackingMode.Stepped;
    }

    [RelayCommand]
    private void UseSmoothCamera()
    {
        CameraTrackingMode = FumeCameraTrackingMode.Smooth;
    }

    [RelayCommand]
    private void Reset()
    {
        BackgroundObjectOpacity = 0.5;
        TextHoldRatio = 1;
        CameraTrackingMode = FumeCameraTrackingMode.Smooth;
        CameraSpeed = 1;
        GlowIntensity = 1;
        HeroScale = 1;
    }

    partial void OnBackgroundObjectOpacityChanged(double value)
    {
        var normalized = Math.Clamp(value, 0, 1);
        if (normalized != value)
        {
            BackgroundObjectOpacity = normalized;
            return;
        }

        _settings.BackgroundObjectOpacity = normalized;
        ScheduleSave();
    }

    partial void OnTextHoldRatioChanged(double value)
    {
        var normalized = Math.Clamp(value, 0, 1);
        if (normalized != value)
        {
            TextHoldRatio = normalized;
            return;
        }

        _settings.TextHoldRatio = normalized;
        ScheduleSave();
    }

    partial void OnCameraTrackingModeChanged(FumeCameraTrackingMode value)
    {
        var normalized = Enum.IsDefined(value) ? value : FumeCameraTrackingMode.Smooth;
        if (normalized != value)
        {
            CameraTrackingMode = normalized;
            return;
        }

        _settings.CameraTrackingMode = normalized;
        ScheduleSave();
    }

    partial void OnCameraSpeedChanged(double value)
    {
        var normalized = Math.Clamp(value, 0.55, 1.85);
        if (normalized != value)
        {
            CameraSpeed = normalized;
            return;
        }

        _settings.CameraSpeed = normalized;
        ScheduleSave();
    }

    partial void OnGlowIntensityChanged(double value)
    {
        var normalized = Math.Clamp(value, 0, 1.8);
        if (normalized != value)
        {
            GlowIntensity = normalized;
            return;
        }

        _settings.GlowIntensity = normalized;
        ScheduleSave();
    }

    partial void OnHeroScaleChanged(double value)
    {
        var normalized = Math.Clamp(value, 0.82, 1.32);
        if (normalized != value)
        {
            HeroScale = normalized;
            return;
        }

        _settings.HeroScale = normalized;
        ScheduleSave();
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTimerTick;
        FlushSave();
    }

    private void ScheduleSave()
    {
        if (_suppressSave)
            return;

        _hasPendingSave = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnSaveTimerTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        FlushSave();
    }

    private void FlushSave()
    {
        if (!_hasPendingSave)
            return;

        _hasPendingSave = false;
        SettingsManager.Save();
    }

    private static void Normalize(FumeThemeSettings settings)
    {
        settings.BackgroundObjectOpacity = Math.Clamp(settings.BackgroundObjectOpacity, 0, 1);
        settings.TextHoldRatio = Math.Clamp(settings.TextHoldRatio, 0, 1);
        settings.CameraSpeed = Math.Clamp(settings.CameraSpeed, 0.55, 1.85);
        settings.GlowIntensity = Math.Clamp(settings.GlowIntensity, 0, 1.8);
        settings.HeroScale = Math.Clamp(settings.HeroScale, 0.82, 1.32);
        if (!Enum.IsDefined(settings.CameraTrackingMode))
            settings.CameraTrackingMode = FumeCameraTrackingMode.Smooth;
    }
}
