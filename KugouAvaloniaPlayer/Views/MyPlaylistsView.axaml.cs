using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnPlaylistCardClick(object? sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button?.DataContext is not PlaylistItem item) return;

        // 获取 VM
        if (DataContext is not MyPlaylistsViewModel vm) return;

        if (item.Type == PlaylistType.AddButton)
        {
            // --- 点击了“添加”卡片 ---
            // 弹出菜单选择：本地还是在线
            var contextMenu = new ContextMenu();

            var addLocalItem = new MenuItem { Header = "导入本地文件夹..." };
            addLocalItem.Click += AddLocalFolder_Click;

            var addOnlineItem = new MenuItem { Header = "新建在线歌单 (TODO)", IsEnabled = false };

            contextMenu.Items.Add(addLocalItem);
            contextMenu.Items.Add(addOnlineItem);

            contextMenu.Open(button);
        }
        else
        {
            // --- 点击了普通歌单 ---
            // 调用 VM 打开详情
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

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        if (DataContext is not MyPlaylistsViewModel vm) return;

        // 计算当前滚动位置 + 视口高度
        var currentBottom = scrollViewer.Offset.Y + scrollViewer.Viewport.Height;

        // 比较：如果滚动到底部（预留 50px 的缓冲距离），且不是正在加载中
        if (currentBottom >= scrollViewer.Extent.Height - 50)
            // 如果 ViewModel 有加载更多的命令，则执行
            if (vm.LoadMoreCommand.CanExecute(null))
                vm.LoadMoreCommand.Execute(null);
    }
}