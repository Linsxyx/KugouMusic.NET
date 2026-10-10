using System.Numerics;

namespace AvaloniaSilkEffects.Lumiere.Light;

internal static class LumiereDraw
{
    /// <summary>A center-anchored sprite of the given size, rotated about its center (Pixi Sprite, anchor 0.5).</summary>
    public static void Sprite(EffectPrimitiveRenderer primitives, EffectTexture texture, Matrix3x2 root,
        float x, float y, float width, float height, float rotation, float alpha, EffectColor tint) =>
        Sprite(primitives, texture, root, x, y, width, height, rotation, alpha, tint, 0.5f, 0.5f);

    /// <summary>Pixi Sprite: the anchor point (fractions of the size) sits at (x, y), rotation turns about it.</summary>
    public static void Sprite(EffectPrimitiveRenderer primitives, EffectTexture texture, Matrix3x2 root,
        float x, float y, float width, float height, float rotation, float alpha, EffectColor tint,
        float anchorX, float anchorY)
    {
        var transform = Matrix3x2.CreateTranslation(-width * anchorX, -height * anchorY);
        if (rotation != 0) transform *= Matrix3x2.CreateRotation(rotation);
        transform *= Matrix3x2.CreateTranslation(x, y) * root;
        primitives.DrawTexture(texture, transform, new Vector2(width, height), alpha, EffectBlendMode.Alpha, tint);
    }

    /// <summary>
    /// Pixi container transform with skew (skew.y = 0): a = cos r·sx, b = sin r·sx, c = −sin(r − kx)·sy, d = cos(r − kx)·sy.
    /// </summary>
    public static Matrix3x2 Container(float x, float y, float rotation, float scaleX, float scaleY, float skewX = 0) => new(
        MathF.Cos(rotation) * scaleX, MathF.Sin(rotation) * scaleX,
        -MathF.Sin(rotation - skewX) * scaleY, MathF.Cos(rotation - skewX) * scaleY,
        x, y);
}
