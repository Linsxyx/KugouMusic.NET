#if DEBUG
// TEMPORARY: settings redesign snapshot harness. Delete before commit.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KugouAvaloniaPlayer.ViewModels;
using KugouAvaloniaPlayer.Views;

namespace KugouAvaloniaPlayer;

internal static class DebugSnapshot
{
    public static void Attach(Window window, MainWindowViewModel vm)
    {
        var dir = Environment.GetEnvironmentVariable("KG_SNAPSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        window.Opened += async (_, _) =>
        {
            try { await Run(window, vm, dir); }
            catch (Exception ex) { await File.WriteAllTextAsync(Path.Combine(dir, "error.txt"), ex.ToString()); }
            finally { Environment.Exit(0); }
        };
    }

    private static async Task Run(Window window, MainWindowViewModel vm, string dir)
    {
        var only = Environment.GetEnvironmentVariable("KG_SNAPSHOT_ONLY");
        var theme = Environment.GetEnvironmentVariable("KG_SNAPSHOT_THEME");
        if (theme == "dark") Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        if (theme == "light") Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        window.Width = 1100; window.Height = 760;
        await Task.Delay(2500);
        vm.NavigateToUserCommand.Execute(null);
        await Task.Delay(800);
        var view = window.GetVisualDescendants().OfType<SettingView>().First();
        var svm = (SettingViewModel)view.DataContext!;
        foreach (var cat in svm.Categories)
        {
            var subs = cat.Id == "lyrics" ? svm.LyricSections : [""];
            foreach (var sub in subs)
            {
                var name = cat.Id + (sub == "" ? "" : "-" + Array.IndexOf(svm.LyricSections, sub));
                if (!string.IsNullOrEmpty(only) && !name.StartsWith(only)) continue;
                svm.SelectedCategory = cat;
                if (sub != "") svm.SelectedLyricSection = sub;
                await Task.Delay(500);
                Save(window, Path.Combine(dir, name + ".png"));
                var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "ContentScroll");
                if (scroll.Content is Visual content) Save(content, Path.Combine(dir, name + "-full.png"));
            }
        }
        if (string.IsNullOrEmpty(only) || only == "narrow")
        {
            window.Width = 640;
            svm.SelectedCategory = svm.Categories[0];
            await Task.Delay(700);
            Save(window, Path.Combine(dir, "narrow.png"));
        }
    }

    private static void Save(Visual visual, string path)
    {
        var size = visual.Bounds.Size;
        if (size.Width < 1 || size.Height < 1) return;
        using var rtb = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
        rtb.Render(visual);
        rtb.Save(path,PngBitmapEncoderOptions.Default);
    }
}
#endif
