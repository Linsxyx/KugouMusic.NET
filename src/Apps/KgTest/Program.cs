using SimpleAudio;

namespace KgTest;

internal static class Program
{
    private static async Task Main()
    {
        SimpleAudioPlayer.Initialize();
        try
        {
            await using var app = new TerminalApp();
            await app.RunAsync();
        }
        finally
        {
            SimpleAudioPlayer.Free();
        }
    }
}
