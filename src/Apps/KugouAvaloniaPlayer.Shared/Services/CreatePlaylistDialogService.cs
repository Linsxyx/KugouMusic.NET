using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using KugouAvaloniaPlayer.Models;
using KugouAvaloniaPlayer.Services.Jellyfin;
using SukiUI.Dialogs;

namespace KugouAvaloniaPlayer.Services;

public interface ICreatePlaylistDialogService
{
    Task<string?> PromptPlaylistNameAsync(string? defaultValue = null);

    Task<string?> PromptTextAsync(string title, string watermark, string? defaultValue = null,
        string confirmText = "确定");

    Task<LocalPlaylistEditResult?> PromptLocalPlaylistEditAsync(string currentName, string? currentCoverPath);

    Task<JellyfinConnectionOptions?> PromptJellyfinConnectionAsync(JellyfinServerSettings? currentSettings);

    Task<JellyfinLibrary?> PromptJellyfinLibraryAsync(IReadOnlyList<JellyfinLibrary> libraries);
}

public sealed record LocalPlaylistEditResult(string Name, string? CoverPath);

public sealed class CreatePlaylistDialogService(
    ISukiDialogManager dialogManager,
    IFolderPickerService folderPickerService,
    IUiDispatcherService uiDispatcher) : ICreatePlaylistDialogService
{
    public Task<string?> PromptPlaylistNameAsync(string? defaultValue = null)
    {
        return PromptTextAsync("新建歌单", "请输入歌单名称", defaultValue, "创建");
    }

    public Task<string?> PromptTextAsync(
        string title,
        string watermark,
        string? defaultValue = null,
        string confirmText = "确定")
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void ShowDialog()
        {
            var textBox = new TextBox
            {
                PlaceholderText = watermark,
                Text = defaultValue ?? string.Empty,
                Width = 300
            };

            dialogManager.CreateDialog()
                .WithTitle(title)
                .WithContent(textBox)
                .WithActionButton("取消", _ => { tcs.TrySetResult(null); }, true, "Standard", "EscapeDialogCancelButton")
                .WithActionButton(confirmText, _ =>
                {
                    var name = textBox.Text?.Trim();
                    tcs.TrySetResult(string.IsNullOrWhiteSpace(name) ? null : name);
                }, true, "Standard")
                .TryShow();
        }

        uiDispatcher.RunOrPost(ShowDialog);

        return tcs.Task;
    }

    public Task<LocalPlaylistEditResult?> PromptLocalPlaylistEditAsync(string currentName, string? currentCoverPath)
    {
        var tcs = new TaskCompletionSource<LocalPlaylistEditResult?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void ShowDialog()
        {
            var selectedCoverPath = currentCoverPath;
            var nameBox = new TextBox
            {
                PlaceholderText = "请输入歌单名称",
                Text = currentName,
                Width = 340
            };

            var coverText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(selectedCoverPath) ? "未选择自定义封面" : selectedCoverPath,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 340,
                Opacity = 0.75
            };

            var pickCoverButton = new Button
            {
                Content = "选择封面图片...",
                HorizontalAlignment = HorizontalAlignment.Left
            };

            pickCoverButton.Click += async (_, _) =>
            {
                var path = await folderPickerService.PickSingleImageFileAsync("选择本地歌单封面");
                if (string.IsNullOrWhiteSpace(path))
                    return;

                selectedCoverPath = path;
                coverText.Text = path;
            };

            var content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "歌单名称" },
                    nameBox,
                    new TextBlock { Text = "歌单封面" },
                    pickCoverButton,
                    coverText
                }
            };

            dialogManager.CreateDialog()
                .WithTitle("编辑本地歌单")
                .WithContent(content)
                .WithActionButton("取消", _ => { tcs.TrySetResult(null); }, true, "Standard", "EscapeDialogCancelButton")
                .WithActionButton("保存", _ =>
                {
                    var name = nameBox.Text?.Trim();
                    tcs.TrySetResult(string.IsNullOrWhiteSpace(name)
                        ? null
                        : new LocalPlaylistEditResult(name, selectedCoverPath));
                }, true, "Standard")
                .TryShow();
        }

        uiDispatcher.RunOrPost(ShowDialog);

        return tcs.Task;
    }

    public Task<JellyfinConnectionOptions?> PromptJellyfinConnectionAsync(JellyfinServerSettings? currentSettings)
    {
        var tcs = new TaskCompletionSource<JellyfinConnectionOptions?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void ShowDialog()
        {
            var serverBox = new TextBox
            {
                PlaceholderText = "http://127.0.0.1:8096",
                Text = currentSettings?.ServerUrl ?? string.Empty,
                Width = 360
            };
            var userIdBox = new TextBox
            {
                PlaceholderText = "Jellyfin UserId",
                Text = currentSettings?.UserId ?? string.Empty,
                Width = 360
            };
            var apiKeyBox = new TextBox
            {
                PlaceholderText = "API Key",
                Text = currentSettings?.ApiKey ?? string.Empty,
                PasswordChar = '*',
                Width = 360
            };

            var content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "服务器地址" },
                    serverBox,
                    new TextBlock { Text = "UserId" },
                    userIdBox,
                    new TextBlock { Text = "API Key" },
                    apiKeyBox
                }
            };

            dialogManager.CreateDialog()
                .WithTitle("连接 Jellyfin")
                .WithContent(content)
                .WithActionButton("取消", _ => { tcs.TrySetResult(null); }, true, "Standard", "EscapeDialogCancelButton")
                .WithActionButton("连接", _ =>
                {
                    var serverUrl = serverBox.Text?.Trim();
                    var userId = userIdBox.Text?.Trim();
                    var apiKey = apiKeyBox.Text?.Trim();
                    tcs.TrySetResult(
                        string.IsNullOrWhiteSpace(serverUrl) ||
                        string.IsNullOrWhiteSpace(userId) ||
                        string.IsNullOrWhiteSpace(apiKey)
                            ? null
                            : new JellyfinConnectionOptions(serverUrl, userId, apiKey));
                }, true, "Standard")
                .TryShow();
        }

        uiDispatcher.RunOrPost(ShowDialog);
        return tcs.Task;
    }

    public Task<JellyfinLibrary?> PromptJellyfinLibraryAsync(IReadOnlyList<JellyfinLibrary> libraries)
    {
        var tcs = new TaskCompletionSource<JellyfinLibrary?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void ShowDialog()
        {
            var comboBox = new ComboBox
            {
                ItemsSource = libraries,
                SelectedIndex = libraries.Count > 0 ? 0 : -1,
                Width = 360
            };

            comboBox.ItemTemplate = new FuncDataTemplate<JellyfinLibrary>((item, _) =>
                new TextBlock { Text = item?.Name ?? string.Empty });

            var content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "选择要导入的 Jellyfin 音乐媒体库" },
                    comboBox
                }
            };

            dialogManager.CreateDialog()
                .WithTitle("导入 Jellyfin")
                .WithContent(content)
                .WithActionButton("取消", _ => { tcs.TrySetResult(null); }, true, "Standard", "EscapeDialogCancelButton")
                .WithActionButton("导入", _ =>
                {
                    tcs.TrySetResult(comboBox.SelectedItem as JellyfinLibrary);
                }, true, "Standard")
                .TryShow();
        }

        uiDispatcher.RunOrPost(ShowDialog);
        return tcs.Task;
    }
}
