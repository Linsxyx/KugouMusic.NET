using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using SukiUI.Dialogs;

namespace KugouAvaloniaPlayer.Services;

public interface ICreatePlaylistDialogService
{
    Task<string?> PromptPlaylistNameAsync(string? defaultValue = null);

    Task<string?> PromptTextAsync(string title, string watermark, string? defaultValue = null,
        string confirmText = "确定");
}

public sealed class CreatePlaylistDialogService(ISukiDialogManager dialogManager) : ICreatePlaylistDialogService
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
                Watermark = watermark,
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

        if (Dispatcher.UIThread.CheckAccess())
            ShowDialog();
        else
            Dispatcher.UIThread.Post(ShowDialog);

        return tcs.Task;
    }
}