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
using SimpleAudio;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace KugouAvaloniaPlayer;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        SimpleAudioPlayer.Initialize();
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
        collection.AddTransient<DiscoverViewModel>();
        collection.AddTransient<MyPlaylistsViewModel>();
        collection.AddTransient<EqSettingsViewModel>();
        collection.AddTransient<ISingerViewModelFactory, SingerViewModelFactory>();
        collection.AddSingleton<IDesktopLyricViewModelFactory, DesktopLyricViewModelFactory>();

        collection.AddSingleton<PlaybackQueueManager>();
        collection.AddSingleton<LyricsService>();
        collection.AddSingleton<FavoritePlaylistService>();
        collection.AddSingleton<PlayerViewModel>();

        collection.AddTransient<RankViewModel>();

        _serviceProvider = collection.BuildServiceProvider();
        var services = _serviceProvider;

        var vm = services.GetRequiredService<MainWindowViewModel>();
        var playerVm = services.GetRequiredService<PlayerViewModel>();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };

            InitializeTrayIcon(playerVm, desktop);
            desktop.Exit += (s, e) =>
            {
                ShutdownTrayIcon();
                playerVm.Dispose();
                SimpleAudioPlayer.Free();
                _serviceProvider?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}