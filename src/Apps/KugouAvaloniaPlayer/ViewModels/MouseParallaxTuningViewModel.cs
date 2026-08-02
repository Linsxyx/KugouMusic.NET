using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;

namespace KugouAvaloniaPlayer.ViewModels;

public sealed partial class MouseParallaxTuningViewModel : ObservableObject
{
    private readonly MouseParallaxSettings _settings;

    public MouseParallaxTuningViewModel(MouseParallaxSettings settings)
    {
        _settings = settings;
        Normalize(_settings);
        Enabled = _settings.Enabled;
        MaxTilt = _settings.MaxTilt;
        Response = _settings.Response;
        OriginX = _settings.OriginX;
        OriginY = _settings.OriginY;
    }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial double MaxTilt { get; set; }

    [ObservableProperty]
    public partial double Response { get; set; }

    [ObservableProperty]
    public partial double OriginX { get; set; }

    [ObservableProperty]
    public partial double OriginY { get; set; }

    [RelayCommand]
    private void Reset()
    {
        Enabled = false;
        MaxTilt = 16;
        Response = 7.5;
        OriginX = 0.3;
        OriginY = 0.5;
    }

    partial void OnEnabledChanged(bool value)
    {
        _settings.Enabled = value;
        SettingsManager.Save();
    }

    partial void OnMaxTiltChanged(double value)
    {
        var normalized = Math.Clamp(value, 0, 40);
        if (Math.Abs(normalized - value) > 1e-5)
        {
            MaxTilt = normalized;
            return;
        }

        _settings.MaxTilt = normalized;
        SettingsManager.Save();
    }

    partial void OnResponseChanged(double value)
    {
        var normalized = Math.Clamp(value, 0.5, 20);
        if (Math.Abs(normalized - value) > 1e-5)
        {
            Response = normalized;
            return;
        }

        _settings.Response = normalized;
        SettingsManager.Save();
    }

    partial void OnOriginXChanged(double value)
    {
        var normalized = Math.Clamp(value, 0, 1);
        if (Math.Abs(normalized - value) > 1e-5)
        {
            OriginX = normalized;
            return;
        }

        _settings.OriginX = normalized;
        SettingsManager.Save();
    }

    partial void OnOriginYChanged(double value)
    {
        var normalized = Math.Clamp(value, 0, 1);
        if (Math.Abs(normalized - value) > 1e-5)
        {
            OriginY = normalized;
            return;
        }

        _settings.OriginY = normalized;
        SettingsManager.Save();
    }

    private static void Normalize(MouseParallaxSettings settings)
    {
        settings.MaxTilt = Math.Clamp(settings.MaxTilt, 0, 40);
        settings.Response = Math.Clamp(settings.Response, 0.5, 20);
        settings.OriginX = Math.Clamp(settings.OriginX, 0, 1);
        settings.OriginY = Math.Clamp(settings.OriginY, 0, 1);
    }
}
