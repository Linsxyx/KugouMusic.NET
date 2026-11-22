using System.Diagnostics;
using System.Runtime.InteropServices;
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

        CliLog.Initialize();
        CliLog.RegisterGlobalExceptionHandlers();
        var version = Environment.GetEnvironmentVariable("KAMUSIC_VERSION");
        if (string.IsNullOrWhiteSpace(version))
        {
            version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "dev";
        }

        CliLog.Info(
            "Program",
            $"CLI 启动 pid={Environment.ProcessId} version={version} os={RuntimeInformation.OSDescription} arch={RuntimeInformation.ProcessArchitecture}");

        var updateRequested = false;
        var audioInitialized = false;
        try
        {
            SimpleAudioPlayer.Initialize();
            audioInitialized = true;
            await using var app = new TerminalApp();
            await app.RunAsync();
            updateRequested = app.UpdateRequested;
        }
        catch (Exception ex)
        {
            CliLog.Error("Program", "CLI 运行过程中发生未处理异常", ex);
            throw;
        }
        finally
        {
            if (audioInitialized)
            {
                SimpleAudioPlayer.Free();
            }

            CliLog.Info("Program", $"CLI 退出 updateRequested={updateRequested}");
            CliLog.Shutdown();
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
