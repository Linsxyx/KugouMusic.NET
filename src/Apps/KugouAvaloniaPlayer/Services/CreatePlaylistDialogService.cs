using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SukiUI.Dialogs;

namespace KugouAvaloniaPlayer.Services;

public interface ICreatePlaylistDialogService
{
    Task<string?> PromptPlaylistNameAsync(string? defaultValue = null);

    Task<string?> PromptTextAsync(string title, string watermark, string? defaultValue = null,
        string confirmText = "确定");

    Task<LocalPlaylistEditResult?> PromptLocalPlaylistEditAsync(string currentName, string? currentCoverPath);
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
                .WithActionButton("取消", _ => { tcs.TrySetResult(null); }, true, "Standard")
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
                .WithActionButton("取消", _ => { tcs.TrySetResult(null); }, true, "Standard")
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
}
