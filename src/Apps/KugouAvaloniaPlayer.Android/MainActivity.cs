using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace KugouAvaloniaPlayer.Android;

[Activity(
    Label = "KA Music",
    Theme = "@style/KugouTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
