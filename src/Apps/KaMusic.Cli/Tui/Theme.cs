using KaMusic.Cli.Graphics;

namespace KaMusic.Cli.Tui;

// 终端界面配色：支持深色与浅色两套调色板，前景色分别按深/浅底调校，背景尽量沿用终端默认背景。

internal sealed class Theme
{
    public bool IsLight { get; init; }
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

    public static Theme Dark { get; } = new();

    public static Theme Light { get; } = new()
    {
        IsLight = true,
        Accent = Rgb.FromHex(0x0969DA),
        Text = Rgb.FromHex(0x1F2328),
        Muted = Rgb.FromHex(0x525B66),
        Faint = Rgb.FromHex(0x7A838E),
        Border = Rgb.FromHex(0xAEB7C2),
        Playing = Rgb.FromHex(0x116E33),
        Warning = Rgb.FromHex(0x9A4807),
        Error = Rgb.FromHex(0xCF222E),
        SelectionBg = Rgb.FromHex(0xCCE2FF),
        SelectionDimBg = Rgb.FromHex(0xE6ECF2),
        PanelBg = Rgb.FromHex(0xF6F8FA),
        InputBg = Rgb.FromHex(0xEAEFF5),
        InputFocusBg = Rgb.FromHex(0xDCE8F8)
    };

    public static Theme For(TerminalThemeKind kind) => kind == TerminalThemeKind.Light ? Light : Dark;

    /// <summary>
    ///     将封面提取的动态强调色适配到当前浅/深色背景，避免在浅底过亮或深底过暗。
    /// </summary>
    public Rgb AdaptAccent(Rgb accent)
    {
        if (IsLight)
        {
            if (accent.Luminance > 0.42f)
            {
                var amount = Math.Clamp((accent.Luminance - 0.42f) * 1.35f + 0.12f, 0f, 0.72f);
                return accent.Mix(Rgb.FromHex(0x161B22), amount);
            }

            return accent;
        }

        return accent.Luminance < 0.45f
            ? accent.Mix(new Rgb(255, 255, 255), 0.45f - accent.Luminance + 0.15f)
            : accent;
    }

    /// <summary>
    ///     为实色高亮块（如按钮、激活标签）选择高对比度前景色。
    /// </summary>
    public Rgb ContrastFg(Rgb background) =>
        background.Luminance > 0.45f ? Rgb.FromHex(0x161B22) : Rgb.FromHex(0xF6F8FA);

    public CellStyle TextStyle => new(Text);
    public CellStyle MutedStyle => new(Muted);
    public CellStyle FaintStyle => new(Faint);
    public CellStyle BorderStyle => new(Border);
    public CellStyle AccentStyle => new(Accent);
    public CellStyle Bold => new(Text, Attr: TextAttr.Bold);
    public CellStyle AccentBold => new(Accent, Attr: TextAttr.Bold);
}
