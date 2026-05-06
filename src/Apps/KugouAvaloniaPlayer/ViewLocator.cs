using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using KugouAvaloniaPlayer.ViewModels;
using KugouAvaloniaPlayer.Views;

namespace KugouAvaloniaPlayer;

/// <summary>
///     Given a view model, returns the corresponding view if possible.
/// </summary>
public class ViewLocator : IDataTemplate
{
    private static readonly IReadOnlyDictionary<Type, Func<Control>> ViewFactories =
        new Dictionary<Type, Func<Control>>
        {
            [typeof(LoginViewModel)] = static () => new LoginView(),
            [typeof(SearchViewModel)] = static () => new SearchView(),
            [typeof(SingerViewModel)] = static () => new SingerView(),
            [typeof(UserViewModel)] = static () => new UserView(),
            [typeof(RankViewModel)] = static () => new RankView(),
            [typeof(DailyRecommendViewModel)] = static () => new DailyRecommendView(),
            [typeof(HistoryViewModel)] = static () => new HistoryView(),
            [typeof(MyPlaylistsViewModel)] = static () => new MyPlaylistsView(),
            [typeof(DiscoverViewModel)] = static () => new DiscoverView()
        };

    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var vmType = param.GetType();
        if (ViewFactories.TryGetValue(vmType, out var factory))
            return factory();

        return new TextBlock { Text = $"Not Found: {vmType.FullName}" };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase or PageViewModelBase;
    }
}
