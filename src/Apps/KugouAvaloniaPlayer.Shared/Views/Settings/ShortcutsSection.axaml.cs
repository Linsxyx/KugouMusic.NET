using Avalonia.Controls;

namespace KugouAvaloniaPlayer.Views.Settings;

public partial class ShortcutsSection : UserControl
{
    public ShortcutsSection() => InitializeComponent();

    private void OnRecordingLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ViewModels.GlobalShortcutItemViewModel { IsRecording: true } } && DataContext is ViewModels.SettingViewModel vm)
            vm.CancelShortcutRecordingCommand.Execute(null);
    }
}
