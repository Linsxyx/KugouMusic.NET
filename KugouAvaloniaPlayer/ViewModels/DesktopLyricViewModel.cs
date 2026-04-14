using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace KugouAvaloniaPlayer.ViewModels;

public partial class DesktopLyricViewModel(PlayerViewModel player, bool canMousePassthrough) : ViewModelBase
{
    [ObservableProperty] private double _fontSize = 30;

    [ObservableProperty] private bool _isLocked;

    public bool CanMousePassthrough { get; } = canMousePassthrough;

    public PlayerViewModel Player { get; } = player;

    [RelayCommand]
    private void ToggleLock()
    {
        IsLocked = !IsLocked;
    }
}
