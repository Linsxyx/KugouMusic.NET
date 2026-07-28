namespace KugouAvaloniaPlayer.Models;

public enum FumeCameraTrackingMode
{
    Stepped,
    Smooth
}

public sealed class FumeThemeSettings
{
    public bool HidePrintSymbols { get; set; }

    public bool DisableGeometricBackground { get; set; } = true;

    public double BackgroundObjectOpacity { get; set; } = 0.5;

    public double TextHoldRatio { get; set; } = 1;

    public FumeCameraTrackingMode CameraTrackingMode { get; set; } =
        FumeCameraTrackingMode.Smooth;

    public double CameraSpeed { get; set; } = 1;

    public double GlowIntensity { get; set; } = 1;

    public double HeroScale { get; set; } = 1;
}
