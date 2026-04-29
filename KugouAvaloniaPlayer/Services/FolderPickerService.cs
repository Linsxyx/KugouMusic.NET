using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace KugouAvaloniaPlayer.Services;

public interface IFolderPickerService
{
    Task<string?> PickSingleFolderAsync(string title);
    Task<string?> PickSingleImageFileAsync(string title);
}

public sealed class FolderPickerService : IFolderPickerService
{
    private static readonly FilePickerFileType ImageFiles = new("图片文件")
    {
        Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.webp", "*.bmp", "*.gif" },
        MimeTypes = new[] { "image/jpeg", "image/png", "image/webp", "image/bmp", "image/gif" }
    };

    public async Task<string?> PickSingleFolderAsync(string title)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow == null)
            return null;

        var folders = await desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        if (folders.Count == 0)
            return null;

        return folders[0].Path.LocalPath;
    }

    public async Task<string?> PickSingleImageFileAsync(string title)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow == null)
            return null;

        var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { ImageFiles, FilePickerFileTypes.ImageAll }
        });

        if (files.Count == 0)
            return null;

        return files[0].Path.LocalPath;
    }
}
