using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace KugouAvaloniaPlayer.Views.NowPlayingThemes;

// Hosts the Pendolo renderer and its theme-specific playback chrome.
public partial class PendoloNowPlayingThemeView : UserControl
{
    public PendoloNowPlayingThemeView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
