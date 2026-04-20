using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace KugouAvaloniaPlayer.Services;

public interface IFolderPickerService
{
    Task<string?> PickSingleFolderAsync(string title);
}

public sealed class FolderPickerService : IFolderPickerService
{
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
}