using Avalonia.Controls;

namespace KugouAvaloniaPlayer.Views;

public partial class EqSettingsView : UserControl
{
    public EqSettingsView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => (DataContext as ViewModels.EqSettingsViewModel)?.Flush();
    }
}