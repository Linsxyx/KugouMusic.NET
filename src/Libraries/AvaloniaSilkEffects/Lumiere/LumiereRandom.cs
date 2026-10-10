namespace AvaloniaSilkEffects.Lumiere;

/// <summary>
/// Folia's lumiereRandom: a mulberry32 stream seeded by FNV-1a of the key. Every random quantity in a
/// Lumiere scene comes from here, so a seed always yields the same frame. JS strings and C# strings are
/// both UTF-16, and mulberry32 is pure 32-bit integer arithmetic, so the streams match Folia bit for bit.
/// </summary>
public sealed class LumiereRng
{
    private const uint Increment = 0x6d2b79f5;
    private uint _state;

    public LumiereRng(string seed) : this(Hash(seed)) { }

    public LumiereRng(uint state) => _state = state;

    /// <summary>The same stream as <c>new LumiereRng(seed)</c>, starting after <paramref name="skip"/> values.</summary>
    public static LumiereRng At(string seed, int skip) => new(unchecked(Hash(seed) + (uint)skip * Increment));

    public static uint Hash(string key)
    {
        var hash = 0x811c9dc5u;
        foreach (var c in key)
        {
            hash ^= c;
            hash = unchecked(hash * 16777619u);
        }
        return hash;
    }

    public double Next()
    {
        unchecked
        {
            _state += Increment;
            var t = (_state ^ (_state >> 15)) * (1u | _state);
            t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }
}
