using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Services;
using SimpleAudio;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class AdvancedAudioEffectsViewModel : ObservableObject
{
    private AdvancedAudioEffectsSettings _settings;
    private readonly PlaybackAudioEffectsService _service;
    private readonly IFolderPickerService _filePicker;
    private bool _loading;
    public event Action? PresetImported;

    public AdvancedAudioEffectsViewModel(PlaybackAudioEffectsService service, IFolderPickerService filePicker)
    {
        _service = service;
        _filePicker = filePicker;
        _settings = SettingsManager.Settings.AdvancedAudioEffects;
        _loading = true;
        StereoEnabled = _settings.StereoEnabled; StereoWidth = _settings.StereoWidth;
        ReverbEnabled = _settings.ReverbEnabled; ReverbAmount = _settings.ReverbAmount; ReverbTimeMs = _settings.ReverbTimeMs;
        EchoEnabled = _settings.EchoEnabled; EchoMix = _settings.EchoMix; EchoFeedback = _settings.EchoFeedback; EchoDelayMs = _settings.EchoDelayMs;
        ChorusEnabled = _settings.ChorusEnabled; ChorusMix = _settings.ChorusMix; ChorusDepth = _settings.ChorusDepth; ChorusRate = _settings.ChorusRate;
        CompressorEnabled = _settings.CompressorEnabled; CompressorThreshold = _settings.CompressorThreshold; CompressorRatio = _settings.CompressorRatio;
        DistortionEnabled = _settings.DistortionEnabled; DistortionDrive = _settings.DistortionDrive; DistortionMix = _settings.DistortionMix;
        BqfEnabled = _settings.BqfEnabled; BqfCenterHz = _settings.BqfCenterHz; BqfGainDb = _settings.BqfGainDb; BqfQ = _settings.BqfQ;
        FlangerEnabled = _settings.FlangerEnabled; FlangerMix = _settings.FlangerMix; FlangerDepth = _settings.FlangerDepth; FlangerRate = _settings.FlangerRate;
        PhaserEnabled = _settings.PhaserEnabled; PhaserMix = _settings.PhaserMix; PhaserRate = _settings.PhaserRate; PhaserRange = _settings.PhaserRange; PhaserFrequency = _settings.PhaserFrequency;
        GargleEnabled = _settings.GargleEnabled; GargleRateHz = _settings.GargleRateHz; AutoWahEnabled = _settings.AutoWahEnabled; AutoWahMix = _settings.AutoWahMix; AutoWahRate = _settings.AutoWahRate; AutoWahRange = _settings.AutoWahRange; AutoWahFrequency = _settings.AutoWahFrequency;
        DampEnabled = _settings.DampEnabled; DampTarget = _settings.DampTarget; DampQuiet = _settings.DampQuiet; DampRate = _settings.DampRate; DampGain = _settings.DampGain; DampDelay = _settings.DampDelay;
        _loading = false;
    }

    public string EnabledSummary => $"已启用 {new[] { StereoEnabled, ReverbEnabled, EchoEnabled, ChorusEnabled, CompressorEnabled, DistortionEnabled, BqfEnabled, FlangerEnabled, PhaserEnabled, GargleEnabled, AutoWahEnabled, DampEnabled }.Count(x => x)} 个效果";
    [ObservableProperty] public partial string ImportExportStatus { get; set; } = "";
    [ObservableProperty] public partial bool StereoEnabled { get; set; }
    [ObservableProperty] public partial float StereoWidth { get; set; }
    [ObservableProperty] public partial bool ReverbEnabled { get; set; }
    [ObservableProperty] public partial float ReverbAmount { get; set; }
    [ObservableProperty] public partial float ReverbTimeMs { get; set; }
    [ObservableProperty] public partial bool EchoEnabled { get; set; }
    [ObservableProperty] public partial float EchoMix { get; set; }
    [ObservableProperty] public partial float EchoFeedback { get; set; }
    [ObservableProperty] public partial float EchoDelayMs { get; set; }
    [ObservableProperty] public partial bool ChorusEnabled { get; set; }
    [ObservableProperty] public partial float ChorusMix { get; set; }
    [ObservableProperty] public partial float ChorusDepth { get; set; }
    [ObservableProperty] public partial float ChorusRate { get; set; }
    [ObservableProperty] public partial bool CompressorEnabled { get; set; }
    [ObservableProperty] public partial float CompressorThreshold { get; set; }
    [ObservableProperty] public partial float CompressorRatio { get; set; }
    [ObservableProperty] public partial bool DistortionEnabled { get; set; }
    [ObservableProperty] public partial float DistortionDrive { get; set; }
    [ObservableProperty] public partial float DistortionMix { get; set; }
    [ObservableProperty] public partial bool BqfEnabled { get; set; }
    [ObservableProperty] public partial float BqfCenterHz { get; set; }
    [ObservableProperty] public partial float BqfGainDb { get; set; }
    [ObservableProperty] public partial float BqfQ { get; set; }
    [ObservableProperty] public partial bool FlangerEnabled { get; set; } [ObservableProperty] public partial float FlangerMix { get; set; } [ObservableProperty] public partial float FlangerDepth { get; set; } [ObservableProperty] public partial float FlangerRate { get; set; }
    [ObservableProperty] public partial bool PhaserEnabled { get; set; } [ObservableProperty] public partial float PhaserMix { get; set; } [ObservableProperty] public partial float PhaserRate { get; set; } [ObservableProperty] public partial float PhaserRange { get; set; } [ObservableProperty] public partial float PhaserFrequency { get; set; }
    [ObservableProperty] public partial bool GargleEnabled { get; set; } [ObservableProperty] public partial int GargleRateHz { get; set; } [ObservableProperty] public partial bool AutoWahEnabled { get; set; } [ObservableProperty] public partial float AutoWahMix { get; set; } [ObservableProperty] public partial float AutoWahRate { get; set; } [ObservableProperty] public partial float AutoWahRange { get; set; } [ObservableProperty] public partial float AutoWahFrequency { get; set; }
    [ObservableProperty] public partial bool DampEnabled { get; set; } [ObservableProperty] public partial float DampTarget { get; set; } [ObservableProperty] public partial float DampQuiet { get; set; } [ObservableProperty] public partial float DampRate { get; set; } [ObservableProperty] public partial float DampGain { get; set; } [ObservableProperty] public partial float DampDelay { get; set; }

    partial void OnStereoEnabledChanged(bool value) => Apply(); partial void OnStereoWidthChanged(float value) => Apply();
    partial void OnReverbEnabledChanged(bool value) => Apply(); partial void OnReverbAmountChanged(float value) => Apply(); partial void OnReverbTimeMsChanged(float value) => Apply();
    partial void OnEchoEnabledChanged(bool value) => Apply(); partial void OnEchoMixChanged(float value) => Apply(); partial void OnEchoFeedbackChanged(float value) => Apply(); partial void OnEchoDelayMsChanged(float value) => Apply();
    partial void OnChorusEnabledChanged(bool value) => Apply(); partial void OnChorusMixChanged(float value) => Apply(); partial void OnChorusDepthChanged(float value) => Apply(); partial void OnChorusRateChanged(float value) => Apply();
    partial void OnCompressorEnabledChanged(bool value) => Apply(); partial void OnCompressorThresholdChanged(float value) => Apply(); partial void OnCompressorRatioChanged(float value) => Apply();
    partial void OnDistortionEnabledChanged(bool value) => Apply(); partial void OnDistortionDriveChanged(float value) => Apply(); partial void OnDistortionMixChanged(float value) => Apply();
    partial void OnBqfEnabledChanged(bool value) => Apply(); partial void OnBqfCenterHzChanged(float value) => Apply(); partial void OnBqfGainDbChanged(float value) => Apply(); partial void OnBqfQChanged(float value) => Apply();
    partial void OnFlangerEnabledChanged(bool value) => Apply(); partial void OnFlangerMixChanged(float value) => Apply(); partial void OnFlangerDepthChanged(float value) => Apply(); partial void OnFlangerRateChanged(float value) => Apply(); partial void OnPhaserEnabledChanged(bool value) => Apply(); partial void OnPhaserMixChanged(float value) => Apply(); partial void OnPhaserRateChanged(float value) => Apply(); partial void OnPhaserRangeChanged(float value) => Apply(); partial void OnPhaserFrequencyChanged(float value) => Apply(); partial void OnGargleEnabledChanged(bool value) => Apply(); partial void OnGargleRateHzChanged(int value) => Apply(); partial void OnAutoWahEnabledChanged(bool value) => Apply(); partial void OnAutoWahMixChanged(float value) => Apply(); partial void OnAutoWahRateChanged(float value) => Apply(); partial void OnAutoWahRangeChanged(float value) => Apply(); partial void OnAutoWahFrequencyChanged(float value) => Apply(); partial void OnDampEnabledChanged(bool value) => Apply(); partial void OnDampTargetChanged(float value) => Apply(); partial void OnDampQuietChanged(float value) => Apply(); partial void OnDampRateChanged(float value) => Apply(); partial void OnDampGainChanged(float value) => Apply(); partial void OnDampDelayChanged(float value) => Apply();

    private void Apply()
    {
        if (_loading) return;
        _settings.StereoEnabled = StereoEnabled; _settings.StereoWidth = Math.Clamp(StereoWidth, 0, 1);
        _settings.ReverbEnabled = ReverbEnabled; _settings.ReverbAmount = Math.Clamp(ReverbAmount, 0, 1); _settings.ReverbTimeMs = Math.Clamp(ReverbTimeMs, 100, 3000);
        _settings.EchoEnabled = EchoEnabled; _settings.EchoMix = Math.Clamp(EchoMix, 0, 1); _settings.EchoFeedback = Math.Clamp(EchoFeedback, 0, .95f); _settings.EchoDelayMs = Math.Clamp(EchoDelayMs, 1, 1000);
        _settings.ChorusEnabled = ChorusEnabled; _settings.ChorusMix = Math.Clamp(ChorusMix, 0, 1); _settings.ChorusDepth = Math.Clamp(ChorusDepth, 0, 100); _settings.ChorusRate = Math.Clamp(ChorusRate, .01f, 10);
        _settings.CompressorEnabled = CompressorEnabled; _settings.CompressorThreshold = Math.Clamp(CompressorThreshold, -60, 0); _settings.CompressorRatio = Math.Clamp(CompressorRatio, 1, 20);
        _settings.DistortionEnabled = DistortionEnabled; _settings.DistortionDrive = Math.Clamp(DistortionDrive, 0, 1); _settings.DistortionMix = Math.Clamp(DistortionMix, 0, 1);
        _settings.BqfEnabled = BqfEnabled; _settings.BqfCenterHz = Math.Clamp(BqfCenterHz, 20, 20000); _settings.BqfGainDb = Math.Clamp(BqfGainDb, -24, 24); _settings.BqfQ = Math.Clamp(BqfQ, .1f, 10);
        _settings.FlangerEnabled = FlangerEnabled; _settings.FlangerMix = Math.Clamp(FlangerMix, 0, 1); _settings.FlangerDepth = Math.Clamp(FlangerDepth, 0, 100); _settings.FlangerRate = Math.Clamp(FlangerRate, .01f, 10);
        _settings.PhaserEnabled = PhaserEnabled; _settings.PhaserMix = Math.Clamp(PhaserMix, 0, 1); _settings.PhaserRate = Math.Clamp(PhaserRate, .01f, 10); _settings.PhaserRange = Math.Clamp(PhaserRange, 0, 10); _settings.PhaserFrequency = Math.Clamp(PhaserFrequency, 20, 20000);
        _settings.GargleEnabled = GargleEnabled; _settings.GargleRateHz = Math.Clamp(GargleRateHz, 1, 2000); _settings.AutoWahEnabled = AutoWahEnabled; _settings.AutoWahMix = Math.Clamp(AutoWahMix, 0, 1); _settings.AutoWahRate = Math.Clamp(AutoWahRate, .01f, 10); _settings.AutoWahRange = Math.Clamp(AutoWahRange, 0, 10); _settings.AutoWahFrequency = Math.Clamp(AutoWahFrequency, 20, 20000);
        _settings.DampEnabled = DampEnabled; _settings.DampTarget = Math.Clamp(DampTarget, 0, 1); _settings.DampQuiet = Math.Clamp(DampQuiet, 0, 1); _settings.DampRate = Math.Clamp(DampRate, 0, 10); _settings.DampGain = Math.Clamp(DampGain, 0, 2); _settings.DampDelay = Math.Clamp(DampDelay, 0, 1000);
        SettingsManager.Save(); _service.ApplyAdvancedEffects(_settings); OnPropertyChanged(nameof(EnabledSummary));
    }

    [RelayCommand] private void DisableAll() { StereoEnabled = ReverbEnabled = EchoEnabled = ChorusEnabled = CompressorEnabled = DistortionEnabled = BqfEnabled = false; Apply(); }

    [RelayCommand]
    private async Task ExportPresetAsync()
    {
        var path = await _filePicker.PickSaveJsonFileAsync("导出音效预设 · Export Preset", "kugou-audio-effects.json");
        if (string.IsNullOrWhiteSpace(path)) return;
        var json = JsonSerializer.Serialize(new AudioEffectsPreset { EqPreset = SettingsManager.Settings.EQPreset, EqGains = SettingsManager.Settings.CustomEqGains, Advanced = _settings }, AppSettingsJsonContext.Default.AudioEffectsPreset);
        await File.WriteAllTextAsync(path, json);
        ImportExportStatus = "预设已导出 · Exported";
    }

    [RelayCommand]
    private async Task ImportPresetAsync()
    {
        var path = await _filePicker.PickJsonFileAsync("导入音效预设 · Import Preset");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var preset = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AudioEffectsPreset);
            if (preset == null || preset.Advanced == null) throw new JsonException();
            _settings = preset.Advanced;
            SettingsManager.Settings.AdvancedAudioEffects = _settings;
            SettingsManager.Settings.EQPreset = preset.EqPreset ?? "自定义";
            SettingsManager.Settings.CustomEqGains = preset.EqGains is { Length: 10 } ? preset.EqGains : new float[10];
            LoadProperties();
            Apply();
            _service.UpdateAudioEffects(SettingsManager.Settings.EQPreset, SettingsManager.Settings.EnableSurround);
            PresetImported?.Invoke();
            ImportExportStatus = "预设已导入 · Imported";
        }
        catch (JsonException) { ImportExportStatus = "JSON 格式无效 · Invalid JSON"; }
        catch (IOException) { ImportExportStatus = "文件读取失败 · Read failed"; }
    }

    private void LoadProperties()
    {
        _loading = true;
        StereoEnabled = _settings.StereoEnabled; StereoWidth = _settings.StereoWidth; ReverbEnabled = _settings.ReverbEnabled; ReverbAmount = _settings.ReverbAmount; ReverbTimeMs = _settings.ReverbTimeMs; EchoEnabled = _settings.EchoEnabled; EchoMix = _settings.EchoMix; EchoFeedback = _settings.EchoFeedback; EchoDelayMs = _settings.EchoDelayMs; ChorusEnabled = _settings.ChorusEnabled; ChorusMix = _settings.ChorusMix; ChorusDepth = _settings.ChorusDepth; ChorusRate = _settings.ChorusRate; CompressorEnabled = _settings.CompressorEnabled; CompressorThreshold = _settings.CompressorThreshold; CompressorRatio = _settings.CompressorRatio; DistortionEnabled = _settings.DistortionEnabled; DistortionDrive = _settings.DistortionDrive; DistortionMix = _settings.DistortionMix; BqfEnabled = _settings.BqfEnabled; BqfCenterHz = _settings.BqfCenterHz; BqfGainDb = _settings.BqfGainDb; BqfQ = _settings.BqfQ; FlangerEnabled = _settings.FlangerEnabled; FlangerMix = _settings.FlangerMix; FlangerDepth = _settings.FlangerDepth; FlangerRate = _settings.FlangerRate; PhaserEnabled = _settings.PhaserEnabled; PhaserMix = _settings.PhaserMix; PhaserRate = _settings.PhaserRate; PhaserRange = _settings.PhaserRange; PhaserFrequency = _settings.PhaserFrequency; GargleEnabled = _settings.GargleEnabled; GargleRateHz = _settings.GargleRateHz; AutoWahEnabled = _settings.AutoWahEnabled; AutoWahMix = _settings.AutoWahMix; AutoWahRate = _settings.AutoWahRate; AutoWahRange = _settings.AutoWahRange; AutoWahFrequency = _settings.AutoWahFrequency; DampEnabled = _settings.DampEnabled; DampTarget = _settings.DampTarget; DampQuiet = _settings.DampQuiet; DampRate = _settings.DampRate; DampGain = _settings.DampGain; DampDelay = _settings.DampDelay;
        _loading = false;
    }
    [RelayCommand] private void Reset() { _loading = true; var d = new AdvancedAudioEffectsSettings(); StereoEnabled = ReverbEnabled = EchoEnabled = ChorusEnabled = CompressorEnabled = DistortionEnabled = BqfEnabled = FlangerEnabled = PhaserEnabled = GargleEnabled = AutoWahEnabled = DampEnabled = false; StereoWidth=d.StereoWidth; ReverbAmount=d.ReverbAmount; ReverbTimeMs=d.ReverbTimeMs; EchoMix=d.EchoMix; EchoFeedback=d.EchoFeedback; EchoDelayMs=d.EchoDelayMs; ChorusMix=d.ChorusMix; ChorusDepth=d.ChorusDepth; ChorusRate=d.ChorusRate; CompressorThreshold=d.CompressorThreshold; CompressorRatio=d.CompressorRatio; DistortionDrive=d.DistortionDrive; DistortionMix=d.DistortionMix; BqfCenterHz=d.BqfCenterHz; BqfGainDb=d.BqfGainDb; BqfQ=d.BqfQ; FlangerMix=d.FlangerMix; FlangerDepth=d.FlangerDepth; FlangerRate=d.FlangerRate; PhaserMix=d.PhaserMix; PhaserRate=d.PhaserRate; PhaserRange=d.PhaserRange; PhaserFrequency=d.PhaserFrequency; GargleRateHz=d.GargleRateHz; AutoWahMix=d.AutoWahMix; AutoWahRate=d.AutoWahRate; AutoWahRange=d.AutoWahRange; AutoWahFrequency=d.AutoWahFrequency; DampTarget=d.DampTarget; DampQuiet=d.DampQuiet; DampRate=d.DampRate; DampGain=d.DampGain; DampDelay=d.DampDelay; _loading = false; Apply(); }
}
