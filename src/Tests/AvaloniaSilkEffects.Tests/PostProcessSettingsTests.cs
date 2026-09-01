namespace AvaloniaSilkEffects.Tests;

public sealed class PostProcessSettingsTests
{
    [Fact]
    public void Reset_DisablesThePipeline()
    {
        var settings = new PostProcessSettings
        {
            Glow = 1,
            Grain = 0.5f,
            Glitch = 0.2f,
        };

        settings.Reset();

        Assert.False(settings.IsEnabled);
    }
}
