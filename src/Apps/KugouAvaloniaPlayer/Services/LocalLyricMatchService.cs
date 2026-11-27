using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.Controls;
using KugouAvaloniaPlayer.ViewModels;

namespace KugouAvaloniaPlayer.Services;

public sealed class LocalLyricMatchService(
    IKugouDialogManager dialogManager,
    IUiDispatcherService uiDispatcher) : ILocalLyricMatchService
{
    public async Task<LocalLyricMatchResult?> MatchLyricAsync(SongItem localSong, List<SongInfo> candidates)
    {
        var tcs = new TaskCompletionSource<LocalLyricMatchResult?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void ShowDialog()
        {
            var viewModel = new LyricMatchDialogViewModel(
                localSong,
                candidates,
                result =>
                {
                    dialogManager.DismissDialog();
                    tcs.TrySetResult(result);
                },
                () =>
                {
                    dialogManager.DismissDialog();
                    tcs.TrySetResult(null);
                });

            dialogManager.CreateDialog()
                .WithContent(new LyricMatchDialog { DataContext = viewModel })
                .TryShow();
        }

        uiDispatcher.RunOrPost(ShowDialog);
        return await tcs.Task;
    }
}
