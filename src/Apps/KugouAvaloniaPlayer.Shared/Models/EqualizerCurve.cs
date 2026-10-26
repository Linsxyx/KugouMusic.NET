using System;

namespace KugouAvaloniaPlayer.Models;

/// <summary>Maps freehand strokes in normalized log-frequency space to the engine's ten bands.</summary>
public static class EqualizerCurve
{
    public static readonly double[] Frequencies = [32, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
    public static double Position(int band) => Math.Log(Frequencies[band] / Frequencies[0]) / Math.Log(500);
    public static float Gain(double y) => (float)Math.Clamp(15 - 30 * y, -15, 15);
    public static int Nearest(double x)
    {
        var best = 0;
        for (var i = 1; i < Frequencies.Length; i++)
            if (Math.Abs(Position(i) - x) < Math.Abs(Position(best) - x)) best = i;
        return best;
    }

    public static void Draw(float[] gains, double fromX, double fromY, double toX, double toY)
    {
        if (gains.Length != 10) throw new ArgumentException("Ten bands are required.", nameof(gains));
        fromX = Math.Clamp(fromX, 0, 1);
        toX = Math.Clamp(toX, 0, 1);
        for (var i = 0; i < gains.Length; i++)
        {
            var x = Position(i);
            if (x < Math.Min(fromX, toX) || x > Math.Max(fromX, toX)) continue;
            var t = Math.Abs(toX - fromX) < 1e-9 ? 1 : (x - fromX) / (toX - fromX);
            gains[i] = Gain(fromY + (toY - fromY) * t);
        }
        // A vertical stroke (or a stroke shorter than a band's spacing) edits the nearest band.
        gains[Nearest(toX)] = Gain(toY);
    }
}

/// <summary>One undo unit per gesture. Presets and their gain snapshots are restored together.</summary>
public sealed class EqualizerEditState
{
    public float[] Gains { get; private set; } = new float[10];
    public string Preset { get; private set; } = "原声";
    private (string Preset, float[] Gains)? _undo;
    public bool CanUndo => _undo.HasValue;

    public void Load(string preset, float[] custom)
    {
        Preset = Array.IndexOf(EqualizerPresets.Names, preset) >= 0 ? preset : "原声";
        Gains = Preset == "自定义" ? Normalize(custom) : EqualizerPresets.GetGains(Preset);
        _undo = null;
    }
    public void BeginEdit() => _undo = (Preset, (float[])Gains.Clone());
    public void SetGains(float[] gains) { Gains = Normalize(gains); Preset = "自定义"; }
    public void SetBand(int band, float gain) { Gains[band] = Math.Clamp(gain, -15, 15); Preset = "自定义"; }
    public void SelectPreset(string preset, float[] custom)
    {
        BeginEdit();
        Preset = preset;
        Gains = preset == "自定义" ? Normalize(custom) : EqualizerPresets.GetGains(preset);
    }
    public void Undo()
    {
        if (_undo is not { } previous) return;
        (Preset, Gains) = previous;
        _undo = null;
    }
    private static float[] Normalize(float[] source)
    {
        var result = new float[10];
        for (var i = 0; i < Math.Min(source.Length, 10); i++)
            result[i] = float.IsFinite(source[i]) ? Math.Clamp(source[i], -15, 15) : 0;
        return result;
    }
}
