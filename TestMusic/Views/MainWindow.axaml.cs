using Avalonia.Interactivity;
using SukiUI.Controls;
using TestMusic.ViewModels;

namespace TestMusic.Views;

public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.MainWindow = this;
    }
}