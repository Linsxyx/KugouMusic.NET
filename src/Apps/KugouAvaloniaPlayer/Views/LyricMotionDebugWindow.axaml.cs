#if DEBUG
using Avalonia.Controls;
using KugouAvaloniaPlayer.Controls;

namespace KugouAvaloniaPlayer.Views;

public partial class LyricMotionDebugWindow : Window
{
    public LyricMotionDebugWindow()
    {
        InitializeComponent();
        DataContext = LyricMotionDebugSettings.Instance;
    }
}
#endif
