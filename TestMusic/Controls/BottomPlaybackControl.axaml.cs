using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TestMusic.Controls;

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