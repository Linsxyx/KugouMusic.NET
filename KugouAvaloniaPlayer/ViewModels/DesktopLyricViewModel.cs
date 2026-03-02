using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class DesktopLyricViewModel(PlayerViewModel player) : ViewModelBase
{
    [ObservableProperty] private double _fontSize = 30;


    [ObservableProperty] private bool _isLocked;

    public PlayerViewModel Player { get; } = player;

    [RelayCommand]
    private void ToggleLock()
    {
        IsLocked = !IsLocked;
    }
}