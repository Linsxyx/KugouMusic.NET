using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TestMusic.Views;

public partial class MyPlaylistsView : UserControl
{
    public MyPlaylistsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}