using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace KugouAvaloniaPlayer.Controls;

public partial class BottomPlaybackControl : UserControl
{
    public BottomPlaybackControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}