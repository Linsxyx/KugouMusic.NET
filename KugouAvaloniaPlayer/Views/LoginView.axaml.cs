using Avalonia.Controls;
using Avalonia.Interactivity;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm) vm.StopQrPolling();
    }
}