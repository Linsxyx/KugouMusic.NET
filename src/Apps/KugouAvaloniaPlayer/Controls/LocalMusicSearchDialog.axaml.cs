using Avalonia.Controls;

namespace KugouAvaloniaPlayer.Controls;

public partial class LocalMusicSearchDialog : UserControl
{
    public LocalMusicSearchDialog()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => SearchBox.Focus();
    }
}
