using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Views;

public partial class MyPlaylistsView : UserControl
{
    public MyPlaylistsView()
    {
        InitializeComponent();
    }

    private void OnPlaylistCardClick(object? sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button?.DataContext is not PlaylistItem item) return;

        // 获取 VM
        if (DataContext is not MyPlaylistsViewModel vm) return;

        if (item.Type == PlaylistType.AddButton)
        {
            var contextMenu = new ContextMenu();

            var addLocalItem = new MenuItem { Header = "导入本地文件夹..." };
            addLocalItem.Click += AddLocalFolder_Click;

            var addOnlineItem = new MenuItem { Header = "新建歌单..." };
            addOnlineItem.Click += AddOnlinePlaylist_Click;

            contextMenu.Items.Add(addLocalItem);
            contextMenu.Items.Add(addOnlineItem);

            contextMenu.Open(button);
        }
        else
        {
            vm.OpenPlaylistCommand.Execute(item);
        }
    }

    private async void AddLocalFolder_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择本地音乐文件夹",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var path = folders[0].Path.LocalPath;
            if (DataContext is MyPlaylistsViewModel vm) vm.AddLocalPlaylist(path);
        }
    }

    private void AddOnlinePlaylist_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MyPlaylistsViewModel vm) vm.ShowCreatePlaylistDialog();
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        if (DataContext is not MyPlaylistsViewModel vm) return;

        var currentBottom = scrollViewer.Offset.Y + scrollViewer.Viewport.Height;

        if (currentBottom >= scrollViewer.Extent.Height - 50)
            if (vm.LoadMoreCommand.CanExecute(null))
                vm.LoadMoreCommand.Execute(null);
    }

    private void OnDeleteLocalPlaylistClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is PlaylistItem item)
            if (DataContext is MyPlaylistsViewModel vm)
                vm.DeleteLocalPlaylistCommand.Execute(item);
    }

    // [新增]：安全响应“删除网络歌单”
    private void OnDeleteOnlinePlaylistClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is PlaylistItem item)
            if (DataContext is MyPlaylistsViewModel vm)
                vm.DeleteOnlinePlaylistCommand.Execute(item);
    }
}