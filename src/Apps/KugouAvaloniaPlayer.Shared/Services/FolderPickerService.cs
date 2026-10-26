using System;
using System.Collections.Generic;
using ZLinq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace KugouAvaloniaPlayer.Services;

public interface IFolderPickerService
{
    Task<string?> PickSingleFolderAsync(string title);
    Task<string?> PickSingleImageFileAsync(string title);
    Task<IReadOnlyList<string>> PickAudioFilesAsync(string title);
}

public sealed class FolderPickerService : IFolderPickerService
{
    private static readonly FilePickerFileType ImageFiles = new("图片文件")
    {
        Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp", "*.bmp", "*.gif"],
        MimeTypes = ["image/jpeg", "image/png", "image/webp", "image/bmp", "image/gif"]
    };

    private static readonly FilePickerFileType AudioFiles = new("音频文件")
    {
        Patterns = ["*.mp3", "*.flac", "*.wav", "*.ogg", "*.m4a", "*.aac", "*.webm", "*.dsf", "*.dff"],
        MimeTypes = ["audio/mpeg", "audio/flac", "audio/wav", "audio/ogg", "audio/mp4", "audio/aac", "audio/webm"]
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
            FileTypeFilter = [ImageFiles, FilePickerFileTypes.ImageAll]
        });

        if (files.Count == 0)
            return null;

        return files[0].Path.LocalPath;
    }

    public async Task<IReadOnlyList<string>> PickAudioFilesAsync(string title)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow == null)
            return Array.Empty<string>();

        var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = [AudioFiles, FilePickerFileTypes.All]
        });

        return files.AsValueEnumerable().Select(x => x.Path.LocalPath).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }
}
