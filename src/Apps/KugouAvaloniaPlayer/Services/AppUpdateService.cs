using System;
using System.Collections.Generic;
using ZLinq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SukiUI.Toasts;
using Velopack;
using Velopack.Sources;

namespace KugouAvaloniaPlayer.Services;

public interface IAppUpdateService
{
    Task CheckForUpdatesAsync(bool showNoUpdateToast = false);
}

public sealed class AppUpdateService(
    ISukiToastManager toastManager,
    ILogger<AppUpdateService> logger) : IAppUpdateService
{
    private const string GitHubRepositoryUrl = "https://github.com/Linsxyx/KugouMusic.NET";
    private const string GiteeReleaseBaseUrl = "https://gitee.com/Linsxyx/KAMusic/releases/download/v1.0.0/";

    public async Task CheckForUpdatesAsync(bool showNoUpdateToast = false)
    {
        try
        {
            var candidates = new[]
            {
                new UpdateSourceCandidate(
                    "GitHub",
                    new UpdateManager(new GithubSource(GitHubRepositoryUrl, null, false)),
                    Priority: 0),
                new UpdateSourceCandidate(
                    "Gitee",
                    new UpdateManager(new SimpleWebSource(GiteeReleaseBaseUrl)),
                    Priority: 1)
            };

            if (!candidates[0].UpdateManager.IsInstalled)
            {
                logger.LogInformation("未通过 Velopack 安装，跳过更新检查。");
                if (showNoUpdateToast)
                    Dispatcher.UIThread.Post(() =>
                    {
                        toastManager.CreateToast()
                            .OfType(NotificationType.Information)
                            .WithTitle("检查更新")
                            .WithContent("应用未通过安装包安装，无法自动更新。")
                            .Dismiss().After(TimeSpan.FromSeconds(3))
                            .Queue();
                    });
                return;
            }

            var checkedCandidates = await CheckAllSourcesAsync(candidates);
            var updateSources = SelectBestVersionSources(checkedCandidates);
            logger.LogInformation("可用更新源选择结果: {Sources}",
                updateSources.Count == 0
                    ? "无"
                    : string.Join("; ", updateSources.AsValueEnumerable().Select(source =>
                        $"{source.SourceName}({source.UpdateInfo.TargetFullRelease.Version}, Priority={source.Priority})").ToArray()));

            if (updateSources.Count == 0)
            {
                logger.LogInformation("当前已是最新版本。");
                if (showNoUpdateToast)
                    Dispatcher.UIThread.Post(() =>
                    {
                        toastManager.CreateToast()
                            .OfType(NotificationType.Success)
                            .WithTitle("检查更新")
                            .WithContent("当前已是最新版本。")
                            .Dismiss().After(TimeSpan.FromSeconds(3))
                            .Queue();
                    });
                return;
            }

            Dispatcher.UIThread.Post(() => ShowActionToast(updateSources));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "检查更新失败。");
            if (showNoUpdateToast)
                Dispatcher.UIThread.Post(() =>
                {
                    toastManager.CreateToast()
                        .OfType(NotificationType.Error)
                        .WithTitle("检查更新失败")
                        .WithContent(ex.Message)
                        .Dismiss().After(TimeSpan.FromSeconds(4))
                        .Queue();
                });
        }
    }

    private async Task<List<CheckedUpdateSource>> CheckAllSourcesAsync(IEnumerable<UpdateSourceCandidate> candidates)
    {
        var checkResults = await Task.WhenAll(candidates.AsValueEnumerable().Select(CheckSourceAsync).ToArray());
        var checkedSources = checkResults
            .AsValueEnumerable().Where(result => result.CheckedSource is not null)
            .Select(result => result.CheckedSource!)
            .ToList();

        var failedSources = checkResults
            .AsValueEnumerable().Where(result => result.Error is not null)
            .ToList();
        if (checkedSources.Count == 0 && failedSources.Count > 0)
        {
            logger.LogWarning("所有更新源检查失败: {Failures}",
                string.Join("; ", failedSources.AsValueEnumerable().Select(result =>
                    $"{result.SourceName}: {DescribeException(result.Error!)}").ToArray()));
        }

        return checkedSources;
    }

    private async Task<UpdateSourceCheckResult> CheckSourceAsync(UpdateSourceCandidate candidate)
    {
        try
        {
            var updateInfo = await candidate.UpdateManager.CheckForUpdatesAsync();
            logger.LogInformation("{SourceName} 更新源检查完成，版本: {Version}",
                candidate.SourceName,
                updateInfo?.TargetFullRelease.Version.ToString() ?? "无更新");
            if (updateInfo is not null)
                LogUpdateInfo(candidate.SourceName, updateInfo);

            return new UpdateSourceCheckResult(
                candidate.SourceName,
                new CheckedUpdateSource(
                    candidate.SourceName,
                    candidate.UpdateManager,
                    updateInfo,
                    candidate.Priority),
                Error: null);
        }
        catch (Exception ex)
        {
            logger.LogWarning("{SourceName} 更新源检查失败: {Error}", candidate.SourceName, DescribeException(ex));
            return new UpdateSourceCheckResult(candidate.SourceName, CheckedSource: null, ex);
        }
    }

    private static string DescribeException(Exception exception)
    {
        return exception.InnerException is null
            ? $"{exception.GetType().Name}: {exception.Message}"
            : $"{exception.GetType().Name}: {exception.Message} Inner={exception.InnerException.GetType().Name}: {exception.InnerException.Message}";
    }

    private static IReadOnlyList<AvailableUpdateSource> SelectBestVersionSources(IEnumerable<CheckedUpdateSource> checkedSources)
    {
        var availableSources = checkedSources
            .AsValueEnumerable().Where(source => source.UpdateInfo != null)
            .Select(source => new AvailableUpdateSource(
                source.SourceName,
                source.UpdateManager,
                source.UpdateInfo!,
                source.Priority))
            .OrderByDescending(source => source.UpdateInfo!.TargetFullRelease.Version)
            .ThenByDescending(source => source.Priority)
            .ToList();

        if (availableSources.Count == 0)
            return [];

        var bestVersion = availableSources[0].UpdateInfo.TargetFullRelease.Version;
        return availableSources
            .AsValueEnumerable().Where(source => source.UpdateInfo.TargetFullRelease.Version.Equals(bestVersion))
            .ToList();
    }

    private void ShowActionToast(IReadOnlyList<AvailableUpdateSource> updateSources)
    {
        var primarySource = updateSources[0];
        toastManager.CreateToast()
            .WithTitle("发现新版本")
            .WithContent($"版本 {primarySource.UpdateInfo.TargetFullRelease.Version} 现已发布，详细可在设置“更新与关于”中查看，是否立即更新？")
            .WithActionButton(CreateStandardToastActionButton("稍后"), _ => { }, true)
            .WithActionButton(CreateStandardToastActionButton("立即更新"), toast =>
            {
                _ = ShowUpdatingToastAndDownloadAsync(updateSources);
            }, true)
            .Queue();
    }

    private async Task ShowUpdatingToastAndDownloadAsync(IReadOnlyList<AvailableUpdateSource> updateSources)
    {
        var progress = new ProgressBar { Value = 0, ShowProgressText = true, Minimum = 0, Maximum = 100 };
        ISukiToast? toast = null;

        var hideButton = new Button
        {
            Content = "x",
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        ToolTip.SetTip(hideButton, "后台继续下载");

        hideButton.Click += (_, _) =>
        {
            if (toast is not null)
                toastManager.Dismiss(toast);
        };

        var sourceStatusText = new TextBlock
        {
            Text = "正在下载更新。",
            Opacity = 0.72,
            VerticalAlignment = VerticalAlignment.Center
        };

        var progressContent = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            RowSpacing = 8
        };
        progressContent.Children.Add(sourceStatusText);
        progressContent.Children.Add(hideButton);
        Grid.SetColumn(hideButton, 1);
        progressContent.Children.Add(progress);
        Grid.SetRow(progress, 1);
        Grid.SetColumnSpan(progress, 2);

        toast = toastManager.CreateToast()
            .WithTitle("正在下载更新...")
            .WithContent(progressContent)
            .Queue();

        Exception? lastException = null;
        try
        {
            foreach (var updateSource in updateSources)
            {
                try
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        progress.Value = 0;
                        sourceStatusText.Text = "正在下载更新。";
                    });
                    logger.LogInformation("开始从 {SourceName} 下载更新 {Version}。",
                        updateSource.SourceName,
                        updateSource.UpdateInfo.TargetFullRelease.Version);
                    LogUpdateInfo(updateSource.SourceName, updateSource.UpdateInfo);

                    await Task.Run(async () =>
                    {
                        var lastLoggedProgress = -10;
                        await updateSource.UpdateManager.DownloadUpdatesAsync(updateSource.UpdateInfo,
                            percentage =>
                            {
                                if (percentage == 100 || percentage >= lastLoggedProgress + 10)
                                {
                                    lastLoggedProgress = percentage;
                                    logger.LogInformation("{SourceName} 更新下载进度: {Progress}%。",
                                        updateSource.SourceName,
                                        percentage);
                                }

                                Dispatcher.UIThread.Post(() => { progress.Value = percentage; });
                            });
                    });
                    logger.LogInformation("从 {SourceName} 下载更新完成: {Package}。",
                        updateSource.SourceName,
                        updateSource.UpdateInfo.TargetFullRelease.FileName);

                    Dispatcher.UIThread.Post(() =>
                    {
                        toastManager.Dismiss(toast);
                        ShowReadyToRestartToast(updateSource.UpdateManager, updateSource.UpdateInfo);
                    });
                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    logger.LogWarning(ex, "从 {SourceName} 下载更新失败。更新包信息: {UpdateInfo}",
                        updateSource.SourceName,
                        DescribeUpdateInfo(updateSource.UpdateInfo));
                    Dispatcher.UIThread.Post(() =>
                    {
                        sourceStatusText.Text = $"{updateSource.SourceName} 下载失败，正在尝试其他更新源。";
                    });
                }
            }

            throw lastException ?? new InvalidOperationException("所有更新源下载均失败。");
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                toastManager.Dismiss(toast);
                toastManager.CreateToast()
                    .OfType(NotificationType.Error)
                    .WithTitle("更新下载失败")
                    .WithContent(ex.Message)
                    .Dismiss().After(TimeSpan.FromSeconds(4))
                    .Queue();
            });
        }
    }

    private void LogUpdateInfo(string sourceName, UpdateInfo updateInfo)
    {
        logger.LogInformation(
            "{SourceName} 更新包详情: Target={Target}; Base={Base}; Deltas={DeltaCount}; DeltaList={DeltaList}; IsDowngrade={IsDowngrade}",
            sourceName,
            FormatAsset(updateInfo.TargetFullRelease),
            updateInfo.BaseRelease is null ? "null" : FormatAsset(updateInfo.BaseRelease),
            updateInfo.DeltasToTarget.Length,
            updateInfo.DeltasToTarget.Length == 0
                ? "无"
                : string.Join(" -> ", updateInfo.DeltasToTarget.AsValueEnumerable().Select(FormatAsset).ToArray()),
            updateInfo.IsDowngrade);
    }

    private static string DescribeUpdateInfo(UpdateInfo updateInfo)
    {
        return $"Target={FormatAsset(updateInfo.TargetFullRelease)}; " +
               $"Base={(updateInfo.BaseRelease is null ? "null" : FormatAsset(updateInfo.BaseRelease))}; " +
               $"Deltas={updateInfo.DeltasToTarget.Length}; " +
               $"DeltaList={(updateInfo.DeltasToTarget.Length == 0 ? "无" : string.Join(" -> ", updateInfo.DeltasToTarget.AsValueEnumerable().Select(FormatAsset).ToArray()))}; " +
               $"IsDowngrade={updateInfo.IsDowngrade}";
    }

    private static string FormatAsset(VelopackAsset asset)
    {
        return $"{asset.FileName} [{asset.Type}, v{asset.Version}, {FormatBytes(asset.Size)}, sha256={asset.SHA256}]";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private void ShowReadyToRestartToast(UpdateManager updateManager, UpdateInfo newVersion)
    {
        toastManager.CreateToast()
            .OfType(NotificationType.Success)
            .WithTitle("更新就绪")
            .WithContent("新版本已下载完毕，重启软件即可应用更新。")
            .WithActionButton(CreateStandardToastActionButton("稍后"), _ => { }, true)
            .WithActionButton(CreateStandardToastActionButton("立即重启"), _ => { updateManager.ApplyUpdatesAndRestart(newVersion); }, true)
            .Queue();
    }

    private static Button CreateStandardToastActionButton(object content)
    {
        var button = new Button
        {
            Content = content,
            Margin = new Thickness(14, 9, 0, 12)
        };

        ApplyClass(button.Classes, "Standard");
        return button;
    }

    private static void ApplyClass(Classes classes, string className)
    {
        if (!classes.Contains(className))
            classes.Add(className);
    }

    private sealed record UpdateSourceCandidate(
        string SourceName,
        UpdateManager UpdateManager,
        int Priority);

    private sealed record CheckedUpdateSource(
        string SourceName,
        UpdateManager UpdateManager,
        UpdateInfo? UpdateInfo,
        int Priority);

    private sealed record UpdateSourceCheckResult(
        string SourceName,
        CheckedUpdateSource? CheckedSource,
        Exception? Error);

    private sealed record AvailableUpdateSource(
        string SourceName,
        UpdateManager UpdateManager,
        UpdateInfo UpdateInfo,
        int Priority);
}
