#if KUGOU_ANDROID
using System.Threading.Tasks;
using KugouAvaloniaPlayer.Models;

namespace KugouAvaloniaPlayer.Services;

// Velopack is desktop-only; Android updates go through the store / APK. TODO(android): check releases instead.
public sealed class AppUpdateService : IAppUpdateService
{
    public Task<UpdateCheckResult> CheckForUpdatesAsync(bool showNoUpdateToast = false) =>
        Task.FromResult(new UpdateCheckResult(UpdateCheckState.Unsupported, "Android 版暂不支持应用内更新。"));
}
#endif
