#if DEBUG
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KugouAvaloniaPlayer.Controls;

public partial class LyricMotionDebugSettings : ObservableObject
{
    public static LyricMotionDebugSettings Instance { get; } = new();

    private LyricMotionDebugSettings()
    {
    }

    public event EventHandler? SettingsChanged;

    [ObservableProperty]
    public partial double StaggerRange { get; set; } = 10;

    [ObservableProperty]
    public partial double StaggerStepMs { get; set; } = 20;

    [ObservableProperty]
    public partial double EntranceStepMs { get; set; } = 12;

    [ObservableProperty]
    public partial double EntranceRiseOffset { get; set; } = 48;

    [ObservableProperty]
    public partial double BaseSpringStiffness { get; set; } = 0.063;

    [ObservableProperty]
    public partial double BaseSpringDamping { get; set; } = 0.72;

    [ObservableProperty]
    public partial double BaseScrollDurationMs { get; set; } = 420;

    [ObservableProperty]
    public partial double ManualOffsetReturnStiffness { get; set; } = 0.052;

    [ObservableProperty]
    public partial double ManualOffsetReturnDamping { get; set; } = 0.78;

    [ObservableProperty]
    public partial double OpacityResponse { get; set; } = 18.0;

    [ObservableProperty]
    public partial double SettleTopThreshold { get; set; } = 0.22;

    [ObservableProperty]
    public partial double SettleVelocityThreshold { get; set; } = 0.12;

    [ObservableProperty]
    public partial double SettleManualOffsetThreshold { get; set; } = 0.35;

    [ObservableProperty]
    public partial double SettleManualVelocityThreshold { get; set; } = 0.2;

    [ObservableProperty]
    public partial double SettleOpacityThreshold { get; set; } = 0.008;

    public int StaggerRangeValue => (int)Math.Round(Math.Clamp(StaggerRange, 0, 40));
    public double StaggerStepSeconds => Math.Max(0, StaggerStepMs) / 1000d;
    public double EntranceStepSeconds => Math.Max(0, EntranceStepMs) / 1000d;

    [RelayCommand]
    private void Reset()
    {
        StaggerRange = 10;
        StaggerStepMs = 20;
        EntranceStepMs = 12;
        EntranceRiseOffset = 48;
        BaseSpringStiffness = 0.063;
        BaseSpringDamping = 0.72;
        BaseScrollDurationMs = 420;
        ManualOffsetReturnStiffness = 0.052;
        ManualOffsetReturnDamping = 0.78;
        OpacityResponse = 18.0;
        SettleTopThreshold = 0.22;
        SettleVelocityThreshold = 0.12;
        SettleManualOffsetThreshold = 0.35;
        SettleManualVelocityThreshold = 0.2;
        SettleOpacityThreshold = 0.008;
    }

    partial void OnStaggerRangeChanged(double value)
    {
        OnPropertyChanged(nameof(StaggerRangeValue));
        NotifySettingsChanged();
    }

    partial void OnStaggerStepMsChanged(double value)
    {
        OnPropertyChanged(nameof(StaggerStepSeconds));
        NotifySettingsChanged();
    }

    partial void OnEntranceStepMsChanged(double value)
    {
        OnPropertyChanged(nameof(EntranceStepSeconds));
        NotifySettingsChanged();
    }

    partial void OnEntranceRiseOffsetChanged(double value) => NotifySettingsChanged();
    partial void OnBaseSpringStiffnessChanged(double value) => NotifySettingsChanged();
    partial void OnBaseSpringDampingChanged(double value) => NotifySettingsChanged();
    partial void OnBaseScrollDurationMsChanged(double value) => NotifySettingsChanged();
    partial void OnManualOffsetReturnStiffnessChanged(double value) => NotifySettingsChanged();
    partial void OnManualOffsetReturnDampingChanged(double value) => NotifySettingsChanged();
    partial void OnOpacityResponseChanged(double value) => NotifySettingsChanged();
    partial void OnSettleTopThresholdChanged(double value) => NotifySettingsChanged();
    partial void OnSettleVelocityThresholdChanged(double value) => NotifySettingsChanged();
    partial void OnSettleManualOffsetThresholdChanged(double value) => NotifySettingsChanged();
    partial void OnSettleManualVelocityThresholdChanged(double value) => NotifySettingsChanged();
    partial void OnSettleOpacityThresholdChanged(double value) => NotifySettingsChanged();

    private void NotifySettingsChanged() => SettingsChanged?.Invoke(this, EventArgs.Empty);
}
#endif
