using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KugouAvaloniaPlayer.Services;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class EqBandViewModel(EqSettingsViewModel owner) : ObservableObject
{
    [ObservableProperty]
    public partial float Value { get; set; }
    public string Frequency { get; init; } = "";
    public int Index { get; init; }

    // 当值改变时通知父级更新
    partial void OnValueChanged(float value)
    {
        owner.OnBandChanged();
    }
}

public partial class EqSettingsViewModel : ObservableObject
{
    private static readonly string[] FreqLabels =
        ["141", "234", "469", "844", "1.3k", "2.2k", "3.7k", "5.8k", "9k", "13.8k"];

    private readonly PlayerViewModel _player;
    private bool _isInitializing;

    public EqSettingsViewModel(PlayerViewModel player)
    {
        _player = player;
        var savedGains = SettingsManager.Settings.CustomEqGains;

        for (var i = 0; i < 10; i++)
            Bands.Add(new EqBandViewModel(this)
            {
                Index = i,
                Frequency = FreqLabels[i],
                Value = savedGains[i]
            });
        _isInitializing = false;
    }

    public ObservableCollection<EqBandViewModel> Bands { get; } = new();

    public void ReloadFromSettings()
    {
        var savedGains = SettingsManager.Settings.CustomEqGains;
        _isInitializing = true;
        for (var i = 0; i < Bands.Count; i++)
            Bands[i].Value = i < savedGains.Length ? savedGains[i] : 0;
        _isInitializing = false;
    }

    public void OnBandChanged()
    {
        if (_isInitializing) return;

        var gains = Bands.Select(b => b.Value).ToArray();
        // 更新到设置中
        SettingsManager.Settings.CustomEqGains = gains;
        SettingsManager.Settings.EQPreset = "自定义";
        SettingsManager.Save();

        // 实时应用到播放器
        _player.ApplyCustomEQ(gains);
    }

    [RelayCommand]
    private void Reset()
    {
        _isInitializing = true;
        foreach (var band in Bands) band.Value = 0;
        _isInitializing = false;
        OnBandChanged();
    }
}
