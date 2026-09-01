using Avalonia.Media;
using System.Numerics;

namespace AvaloniaSilkEffects;

public readonly record struct EffectColor(float R, float G, float B, float A = 1)
{
    public static EffectColor Transparent => new(0, 0, 0, 0);
    public static EffectColor White => new(1, 1, 1, 1);

    public static EffectColor FromAvalonia(Color color) => new(
        color.R / 255f,
        color.G / 255f,
        color.B / 255f,
        color.A / 255f);

    public EffectColor Premultiplied() => new(R * A, G * A, B * A, A);

    internal Vector4 ToVector4() => new(R, G, B, A);
}
