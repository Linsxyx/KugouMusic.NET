using System.Diagnostics;
using System.Text;
using KaMusic.Cli.Services;
using KaMusic.Cli.Tui;
using SimpleAudio;

namespace KaMusic.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        TerminalRenderer.EnableWindowsVirtualTerminalStatic();
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        if (args.Length > 0 &&
            (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase) ||
             args.Contains("--preview-image", StringComparer.OrdinalIgnoreCase)))
        {
            return await CliDiagnostics.RunAsync(args);
        }

        var updateRequested = false;
        SimpleAudioPlayer.Initialize();
        try
        {
            await using var app = new TerminalApp();
            await app.RunAsync();
            updateRequested = app.UpdateRequested;
        }
        finally
        {
            SimpleAudioPlayer.Free();
        }

        if (!updateRequested)
        {
            return 0;
        }

        if (Environment.GetEnvironmentVariable("KAMUSIC_NPM_WRAPPER") == "1")
        {
            return TerminalUpdateService.UpdateExitCode;
        }

        return RunDirectNpmUpdate();
    }

    private static int RunDirectNpmUpdate()
    {
        Console.WriteLine("[kamusic] 正在通过 npm 更新 @linsxyx/kamusic@latest ...");
        var isWin = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWin ? "cmd.exe" : "npm",
            Arguments = isWin
                ? "/c npm install -g @linsxyx/kamusic@latest --prefer-online"
                : "install -g @linsxyx/kamusic@latest --prefer-online",
            UseShellExecute = false
        };
        using var proc = Process.Start(psi);
        proc?.WaitForExit();
        return proc?.ExitCode ?? 1;
    }
}
