namespace KgTest.Tui;

// 终端界面配色。前景色基于深色终端调校，背景尽量沿用终端默认背景。

internal sealed class Theme
{
    public Rgb Accent { get; set; } = Rgb.FromHex(0x3BA4FF);
    public Rgb Text { get; init; } = Rgb.FromHex(0xE6E8EB);
    public Rgb Muted { get; init; } = Rgb.FromHex(0x8A9099);
    public Rgb Faint { get; init; } = Rgb.FromHex(0x4A505A);
    public Rgb Border { get; init; } = Rgb.FromHex(0x3A404A);
    public Rgb Playing { get; init; } = Rgb.FromHex(0x4ADE80);
    public Rgb Warning { get; init; } = Rgb.FromHex(0xFBBF24);
    public Rgb Error { get; init; } = Rgb.FromHex(0xF87171);
    public Rgb SelectionBg { get; init; } = Rgb.FromHex(0x1E3A5F);
    public Rgb SelectionDimBg { get; init; } = Rgb.FromHex(0x252A33);
    public Rgb PanelBg { get; init; } = Rgb.FromHex(0x16191F);
    public Rgb InputBg { get; init; } = Rgb.FromHex(0x22262E);
    public Rgb InputFocusBg { get; init; } = Rgb.FromHex(0x2B3240);

    public CellStyle TextStyle => new(Text);
    public CellStyle MutedStyle => new(Muted);
    public CellStyle FaintStyle => new(Faint);
    public CellStyle BorderStyle => new(Border);
    public CellStyle AccentStyle => new(Accent);
    public CellStyle Bold => new(Text, Attr: TextAttr.Bold);
    public CellStyle AccentBold => new(Accent, Attr: TextAttr.Bold);
}
