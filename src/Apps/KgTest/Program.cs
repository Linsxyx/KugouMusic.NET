using System.Text;
using KgTest.Tui;
using SimpleAudio;

namespace KgTest;

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

        SimpleAudioPlayer.Initialize();
        try
        {
            await using var app = new TerminalApp();
            await app.RunAsync();
            return 0;
        }
        finally
        {
            SimpleAudioPlayer.Free();
        }
    }
}
