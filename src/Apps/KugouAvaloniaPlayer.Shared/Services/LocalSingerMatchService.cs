using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KuGou.Net.Abstractions.Models;
using KugouAvaloniaPlayer.Controls;
using KugouAvaloniaPlayer.ViewModels;
using SukiUI.Dialogs;

namespace KugouAvaloniaPlayer.Services;

public sealed class LocalSingerMatchService(
    ISukiDialogManager dialogManager,
    IUiDispatcherService uiDispatcher) : ILocalSingerMatchService
{
    public async Task<SingerLite?> MatchSingerAsync(SingerLite localSinger, List<SearchAuthorItem> candidates)
    {
        var tcs = new TaskCompletionSource<SingerLite?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void ShowDialog()
        {
            var viewModel = new SingerMatchDialogViewModel(
                localSinger.Name,
                candidates,
                async selected =>
                {
                    dialogManager.DismissDialog();
                    tcs.TrySetResult(selected);
                },
                () =>
                {
                    dialogManager.DismissDialog();
                    tcs.TrySetResult(null);
                });

            dialogManager.CreateDialog()
                .WithContent(new SingerMatchDialog
                {
                    DataContext = viewModel
                })
                .TryShow();
        }

        uiDispatcher.RunOrPost(ShowDialog);

        return await tcs.Task;
    }
}
