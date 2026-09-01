namespace AvaloniaSilkEffects.Tests;

public sealed class DeterministicRandomTests
{
    [Fact]
    public void SameSeed_ReplaysTheSameSequence()
    {
        var first = new DeterministicRandom(42);
        var second = new DeterministicRandom(42);

        for (var index = 0; index < 32; index++)
            Assert.Equal(first.NextUInt(), second.NextUInt());
    }

    [Fact]
    public void Hash_IsStableAndOrderSensitive()
    {
        Assert.Equal(DeterministicRandom.Hash("sonnet"), DeterministicRandom.Hash("sonnet"));
        Assert.NotEqual(DeterministicRandom.Hash("sonnet"), DeterministicRandom.Hash("tennos"));
    }
}
