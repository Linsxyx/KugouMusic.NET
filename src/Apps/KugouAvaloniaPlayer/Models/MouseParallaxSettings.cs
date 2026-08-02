namespace KugouAvaloniaPlayer.Models;

// Tunable parameters for the mouse-parallax 3D tilt shared by the Pendolo and
// Fume now-playing themes. OriginX / OriginY are relative (0-1) coordinates of
// the render pivot, combined into a RelativePoint by the visualizer controls.
public sealed class MouseParallaxSettings
{
    public bool Enabled { get; set; }

    public double MaxTilt { get; set; } = 14;

    public double Response { get; set; } = 7.5;

    public double OriginX { get; set; } = 0.5;

    public double OriginY { get; set; } = 0.5;
}
