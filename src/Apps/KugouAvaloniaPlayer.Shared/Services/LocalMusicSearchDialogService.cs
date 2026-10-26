using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using KugouAvaloniaPlayer.Controls;
using KugouAvaloniaPlayer.ViewModels;
using SukiUI.Dialogs;

namespace KugouAvaloniaPlayer.Services;

public interface ILocalMusicSearchDialogService
{
    void Show(Func<LocalTrackSearchResult, Task> openResultAction);
}

public sealed class LocalMusicSearchDialogService(
    ISukiDialogManager dialogManager,
    ILocalMusicLibraryService localMusicLibraryService,
    IUiDispatcherService uiDispatcher) : ILocalMusicSearchDialogService
{
    public void Show(Func<LocalTrackSearchResult, Task> openResultAction)
    {
        uiDispatcher.RunOrPost(() =>
        {
            LocalMusicSearchDialogViewModel? viewModel = null;

            void Dismiss()
            {
                viewModel?.Dispose();
                dialogManager.DismissDialog();
            }

            viewModel = new LocalMusicSearchDialogViewModel(
                localMusicLibraryService,
                async result =>
                {
                    Dismiss();
                    await openResultAction(result);
                },
                Dismiss);

            dialogManager.CreateDialog()
                .WithContent(new LocalMusicSearchDialog
                {
                    DataContext = viewModel
                })
                .TryShow();
        });
    }
}
