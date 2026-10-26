using System;
using System.IO;
using AvaloniaSilkEffects;
using AvaloniaSilkEffects.Gpu;
using Serilog;

namespace KugouAvaloniaPlayer.Services.Startup;

/// <summary>
/// Chooses the compositor's graphics API at startup: Metal (macOS) or Vulkan (Windows, Linux) first, so the
/// effect themes can render natively, and the previous OpenGL / ANGLE / EGL / GLX stack otherwise.
/// OpenGL effects only render on an OpenGL-family compositor, so when native effects fail at runtime the choice is
/// remembered and the next launch starts on OpenGL again. <c>AVALONIA_SILK_EFFECTS_BACKEND=opengl</c> forces it.
/// </summary>
internal static class GraphicsBackendSelection
{
    private static readonly string MarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "kugou", "gpu-opengl-fallback");

    /// <summary>Whether the compositor should prefer its OpenGL family over Metal / Vulkan.</summary>
    public static bool PreferOpenGl { get; private set; }

    public static void Initialize()
    {
        if (File.Exists(MarkerPath))
        {
            Log.Information("上次原生 GPU 特效初始化失败，本次使用 OpenGL 渲染（删除 {Marker} 可重新尝试）", MarkerPath);
            SilkEffectControl.BackendPreference = EffectGpuBackendPreference.OpenGl;
        }
        PreferOpenGl = SilkEffectControl.BackendPreference == EffectGpuBackendPreference.OpenGl;
        SilkEffectControl.BackendSelected += OnBackendSelected;
    }

    private static void OnBackendSelected(object? sender, EffectBackendSelectedEventArgs e)
    {
        if (e.FallbackReason is null)
        {
            Log.Information("GPU 特效后端：{Backend}", e.Backend);
            return;
        }
        Log.Warning("GPU 特效回退到 {Backend}：{Reason}", e.Backend, e.FallbackReason);
        // Only a native compositor without native effects needs a restart on OpenGL.
        if (PreferOpenGl) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllText(MarkerPath, $"{DateTime.Now:O} {e.FallbackReason}");
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "无法记录 GPU 回退标记");
        }
    }
}
