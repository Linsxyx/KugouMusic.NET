using System;
using System.Threading.Tasks;
using KugouAvaloniaPlayer.Controls;
using KugouAvaloniaPlayer.ViewModels;
using SukiUI.Dialogs;

namespace KugouAvaloniaPlayer.Services;

public interface ITrackPlaylistSearchDialogService
{
    void Show(SongItem song, Func<LocalTrackSearchResult, Task> openResultAction);
}

public sealed class TrackPlaylistSearchDialogService(
    ISukiDialogManager dialogManager,
    ILocalMusicLibraryService localMusicLibraryService,
    IUiDispatcherService uiDispatcher) : ITrackPlaylistSearchDialogService
{
    public void Show(SongItem song, Func<LocalTrackSearchResult, Task> openResultAction)
    {
        uiDispatcher.RunOrPost(() =>
        {
            TrackPlaylistSearchDialogViewModel? viewModel = null;

            TrackPlaylistSearchDialogViewModel? model = viewModel;

            viewModel = new TrackPlaylistSearchDialogViewModel(
                localMusicLibraryService,
                song,
                async result =>
                {
                    Dismiss();
                    await openResultAction(result);
                },
                Dismiss);

            dialogManager.CreateDialog()
                .WithContent(new TrackPlaylistSearchDialog
                {
                    DataContext = viewModel
                })
                .TryShow();
            return;

            void Dismiss()
            {
                model?.Dispose();
                dialogManager.DismissDialog();
            }
        });
    }
}
