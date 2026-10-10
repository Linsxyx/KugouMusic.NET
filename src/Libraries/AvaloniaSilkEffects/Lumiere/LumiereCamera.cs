using System.Numerics;

namespace AvaloniaSilkEffects.Lumiere;

/// <summary>Container transform with Pixi's meaning: subtract the pivot, scale, rotate, move to position.</summary>
public readonly record struct LumiereTransform(double X, double Y, double PivotX, double PivotY, double Scale, double Rotation)
{
    public Matrix3x2 ToMatrix() =>
        Matrix3x2.CreateTranslation((float)-PivotX, (float)-PivotY) *
        Matrix3x2.CreateScale((float)Scale) *
        Matrix3x2.CreateRotation((float)Rotation) *
        Matrix3x2.CreateTranslation((float)X, (float)Y);
}

/// <summary>A section of a seamless unit (camera pushes per section, alternating in and out).</summary>
public readonly record struct LumiereSection(double StartTime, double EndTime);

/// <summary>Folia lumiereUnitLayout.ts camera: slow push + pan eased at both ends, plus handheld float. Pure in t.</summary>
public sealed class LumiereCamera(
    double width,
    double height,
    LumiereCameraSpec camera,
    double startTime,
    double endTime,
    IReadOnlyList<LumiereSection>? sections = null,
    LumiereAnimationIntensity intensity = LumiereAnimationIntensity.Normal)
{
    private readonly double _motion = intensity switch
    {
        LumiereAnimationIntensity.Calm => 0.6,
        LumiereAnimationIntensity.Chaotic => 1.4,
        _ => 1,
    };

    public LumiereTransform Rest => new(width / 2, height / 2, width / 2, height / 2, 1, 0);

    public static double Progress(double time, double startTime, double endTime, IReadOnlyList<LumiereSection>? sections)
    {
        static double Eased(double time, double start, double end)
        {
            var linear = Math.Min(1, Math.Max(0, (time - start) / Math.Max(end - start, 0.001)));
            return (1 - Math.Cos(linear * Math.PI)) / 2;
        }
        if (sections is null || sections.Count == 0) return Eased(time, startTime, endTime);
        var index = 0;
        for (var i = 0; i < sections.Count; i++)
            if (sections[i].StartTime <= time) index = i;
        var section = sections[index];
        var progress = Eased(time, section.StartTime, section.EndTime);
        return index % 2 == 0 ? progress : 1 - progress;
    }

    public LumiereTransform At(double time)
    {
        var progress = Progress(time, startTime, endTime, sections);
        var floatX = (Math.Sin(time * 0.21 + 0.4) * 0.6 + Math.Sin(time * 0.53 + 1.9) * 0.4) * 0.006 * _motion;
        var floatY = (Math.Cos(time * 0.17 + 1.1) * 0.6 + Math.Sin(time * 0.47 + 0.3) * 0.4) * 0.006 * _motion;
        return new LumiereTransform(
            width / 2 + (camera.DriftX * progress + floatX) * width,
            height / 2 + (camera.DriftY * progress + floatY) * height,
            width / 2,
            height / 2,
            (1 + camera.Push * progress) * (1 + 0.008 * _motion * Math.Sin(time * 0.31 + 0.7)),
            0.004 * _motion * Math.Sin(time * 0.13 + 2.1));
    }
}
