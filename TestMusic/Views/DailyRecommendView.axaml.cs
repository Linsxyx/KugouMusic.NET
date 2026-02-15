using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TestMusic.Views;

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