using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using KuGou.Net.Infrastructure;
using KugouAvaloniaPlayer.Services;
using KugouAvaloniaPlayer.ViewModels;
using KugouAvaloniaPlayer.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        BindingPlugins.DataValidators.RemoveAt(0);
        var collection = new ServiceCollection();
        collection.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
            builder.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
            builder.AddDebug();
            builder.AddConsole();
        });
        collection.AddKuGouSdk();

        collection.AddSingleton<ISukiToastManager, SukiToastManager>();
        collection.AddSingleton<ISukiDialogManager, SukiDialogManager>();

        SettingsManager.Load();

        // 注册 ViewModels
        collection.AddTransient<LoginViewModel>();
        collection.AddTransient<SearchViewModel>();
        collection.AddTransient<SingerViewModel>();
        collection.AddTransient<UserViewModel>();
        collection.AddTransient<MainWindowViewModel>();
        collection.AddTransient<DailyRecommendViewModel>();
        collection.AddTransient<MyPlaylistsViewModel>();
        collection.AddSingleton<PlayerViewModel>();

        collection.AddTransient<RankViewModel>();

        var services = collection.BuildServiceProvider();

        var vm = services.GetRequiredService<MainWindowViewModel>();
        var playerVm = services.GetRequiredService<PlayerViewModel>();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };

            InitializeTrayIcon(playerVm, desktop);
            desktop.Exit += (s, e) =>
            {
                ShutdownTrayIcon();
                playerVm.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove) BindingPlugins.DataValidators.Remove(plugin);
    }
}