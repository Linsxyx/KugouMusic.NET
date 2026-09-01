namespace AvaloniaSilkEffects.Sonnet;

public static class SonnetRandom
{
    public static uint Hash(string value)
    {
        uint hash = 2166136261;
        foreach (var character in value)
        {
            hash ^= character;
            hash = unchecked(hash * 16777619);
        }
        return hash;
    }

    public static uint Mix(uint seed, uint salt) => unchecked((seed ^ salt) * 2654435761u);

    public static double Hash01(uint seed, int index, uint salt) =>
        Mix(unchecked(seed + (uint)((index + 1) * 97)), salt) / 4294967296d;
}
