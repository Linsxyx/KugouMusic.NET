using System.Numerics;
using AvaloniaSilkEffects.Lumiere.Light;

namespace AvaloniaSilkEffects.Lumiere;

/// <summary>
/// The two bloomed groups every Lumiere picture is made of (scene units and the credits card): the graphics group (light field,
/// art, motes) drawn into an overscanned target, the text group into a viewport-sized one, each bloomed with its preset and
/// composited (premultiplied over) onto <c>output</c>. Both are in screen space after the camera, like Pixi filters.
/// </summary>
internal static class LumiereGroups
{
    /// <param name="root">Stage logical pixels → group target pixels.</param>
    /// <param name="viewport">Group target size in pixels.</param>
    public delegate void Draw(Matrix3x2 root, Vector2 viewport);

    public static void Render(LumiereGpu gpu, EffectPrimitiveRenderer primitives, LumiereBinding output, float pixelScale,
        Matrix3x2 camera, float width, float height, float overscan, LumiereSceneTuning tuning, Draw? graphics, Draw? text)
    {
        var targetViewport = primitives.Viewport;
        // Folia renders at devicePixelRatio clamped to 1..2; bloom widths are per pixel, so groups do the same.
        var groupScale = MathF.Min(pixelScale, 2);
        primitives.Flush();

        if (graphics is not null)
        {
            var scale = groupScale * Math.Clamp(tuning.GraphicsScale, 0.25f, 1);
            var target = gpu.GraphicsTarget;
            target.EnsureSize((int)MathF.Ceiling((width + overscan * 2) * scale), (int)MathF.Ceiling((height + overscan * 2) * scale));
            target.Bind(clear: true);
            var viewport = new Vector2(target.Width, target.Height);
            primitives.SetViewport(viewport);
            graphics(camera * Matrix3x2.CreateTranslation(overscan, overscan) * Matrix3x2.CreateScale(scale), viewport);
            primitives.Flush();
            var map = new Vector4(width * scale / target.Width, height * scale / target.Height,
                overscan * scale / target.Width, overscan * scale / target.Height);
            gpu.GraphicsBloom.Apply(target, output, LumiereBloomPreset.Graphics, tuning.Bloom, map);
            primitives.RecordExternalDrawCalls(gpu.GraphicsBloom.DrawCalls + 1);
        }

        if (text is not null)
        {
            var target = gpu.TextTarget;
            target.EnsureSize((int)MathF.Ceiling(width * groupScale), (int)MathF.Ceiling(height * groupScale));
            target.Bind(clear: true);
            var viewport = new Vector2(target.Width, target.Height);
            primitives.SetViewport(viewport);
            text(camera * Matrix3x2.CreateScale(groupScale), viewport);
            primitives.Flush();
            var map = new Vector4(width * groupScale / target.Width, height * groupScale / target.Height, 0, 0);
            gpu.TextBloom.Apply(target, output, LumiereBloomPreset.Text, tuning.TextBloom, map);
            primitives.RecordExternalDrawCalls(gpu.TextBloom.DrawCalls + 1);
        }

        primitives.SetViewport(targetViewport);
    }
}
