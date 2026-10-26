using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class EqBandViewModel(EqSettingsViewModel owner) : ObservableObject
{
    [ObservableProperty] public partial float Value { get; set; }
    public string Frequency { get; init; } = "";
    public int Index { get; init; }
    partial void OnValueChanged(float value) => owner.SetBand(Index, value);
}

public partial class EqSettingsViewModel : PageViewModelBase
{
    private readonly PlayerViewModel _player;
    private readonly INavigationService _navigation;
    private readonly EqualizerEditState _state = new();
    private readonly DispatcherTimer _applyTimer;
    private readonly DispatcherTimer _saveTimer;
    private bool _synchronizing;
    private bool _dirty;
    private bool _pendingAudio;
    private float[]? _customBeforeEdit;
    public override string DisplayName => "均衡器";
    public override string Icon => "";
    public event Action? CurveChanged;
    public event Action? PresetChanged;
    public ObservableCollection<EqBandViewModel> Bands { get; } = new();
    public string[] Presets => EqualizerPresets.Names;
    public float[] Gains => _state.Gains;
    public bool CanUndo => _state.CanUndo;
    [ObservableProperty] public partial string SelectedPreset { get; set; } = "原声";

    public EqSettingsViewModel(PlayerViewModel player, INavigationService navigation)
    {
        _player = player;
        _navigation = navigation;
        _applyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _applyTimer.Tick += (_, _) => ApplyPending();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _saveTimer.Tick += (_, _) => Flush();
        string[] labels = ["32 Hz", "63 Hz", "125 Hz", "250 Hz", "500 Hz", "1 kHz", "2 kHz", "4 kHz", "8 kHz", "16 kHz"];
        for (var i = 0; i < labels.Length; i++) Bands.Add(new EqBandViewModel(this) { Index = i, Frequency = labels[i] });
        ReloadFromSettings();
    }

    public void ReloadFromSettings()
    {
        Flush();
        _state.Load(SettingsManager.Settings.EQPreset, SettingsManager.Settings.CustomEqGains);
        Synchronize();
    }
    private void BeginEdit()
    {
        _customBeforeEdit = (float[])SettingsManager.Settings.CustomEqGains.Clone();
        _state.BeginEdit();
    }
    public void BeginStroke() => BeginEdit();
    public void Draw(double fromX, double fromY, double toX, double toY)
    {
        var gains = (float[])_state.Gains.Clone();
        EqualizerCurve.Draw(gains, fromX, fromY, toX, toY);
        _state.SetGains(gains);
        Changed();
    }
    public void SetBand(int band, float gain)
    {
        if (_synchronizing) return;
        BeginEdit();
        _state.SetBand(band, gain);
        Changed();
    }
    partial void OnSelectedPresetChanged(string value)
    {
        if (_synchronizing) return;
        _customBeforeEdit = (float[])SettingsManager.Settings.CustomEqGains.Clone();
        _state.SelectPreset(value, SettingsManager.Settings.CustomEqGains);
        Changed();
        Flush();
    }
    private void Changed()
    {
        Synchronize();
        SettingsManager.Settings.EQPreset = _state.Preset;
        if (_state.Preset == "自定义") SettingsManager.Settings.CustomEqGains = (float[])_state.Gains.Clone();
        _dirty = _pendingAudio = true;
        if (!_applyTimer.IsEnabled) _applyTimer.Start();
        _saveTimer.Stop();
        _saveTimer.Start();
        PresetChanged?.Invoke();
    }
    private void Synchronize()
    {
        _synchronizing = true;
        try
        {
            SelectedPreset = _state.Preset;
            for (var i = 0; i < Bands.Count; i++) Bands[i].Value = _state.Gains[i];
        }
        finally { _synchronizing = false; }
        OnPropertyChanged(nameof(CanUndo));
        CurveChanged?.Invoke();
    }
    private void ApplyPending()
    {
        _applyTimer.Stop();
        if (!_pendingAudio) return;
        _pendingAudio = false;
        _player.ApplyCustomEQ((float[])_state.Gains.Clone());
    }
    public void Flush()
    {
        ApplyPending();
        _saveTimer.Stop();
        if (!_dirty) return;
        _dirty = false;
        SettingsManager.Save();
    }
    [RelayCommand] private void Reset()
    {
        BeginEdit();
        _state.SetGains(new float[10]);
        Changed();
        Flush();
    }
    [RelayCommand] private void Undo()
    {
        if (!_state.CanUndo) return;
        _state.Undo();
        if (_customBeforeEdit != null) SettingsManager.Settings.CustomEqGains = _customBeforeEdit;
        Changed();
        Flush();
    }
    [RelayCommand] private void Back() { Flush(); _navigation.GoBack(); }
}
