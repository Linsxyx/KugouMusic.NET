using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace KugouAvaloniaPlayer.Controls;

public partial class  EqSettingsControl : UserControl
{
    public EqSettingsControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}