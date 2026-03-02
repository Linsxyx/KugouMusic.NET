using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace KugouAvaloniaPlayer.Views;

public partial class DailyRecommendView : UserControl
{
    public DailyRecommendView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}