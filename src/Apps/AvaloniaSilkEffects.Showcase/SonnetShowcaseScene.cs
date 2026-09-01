using Avalonia;
using AvaloniaSilkEffects.Sonnet;

namespace AvaloniaSilkEffects.Showcase;

internal sealed class SonnetShowcaseScene : ShowcaseScene
{
    private readonly SonnetTuning _tuning = new();
    private readonly SonnetScene _scene;

    public SonnetShowcaseScene()
    {
        var theme = new SonnetTheme(
            new(0.015f, 0.018f, 0.03f),
            new(0.93f, 0.94f, 1f),
            new(0.08f, 0.87f, 0.98f),
            new(0.67f, 0.48f, 0.86f),
            "PingFang SC");
        _scene = new(SonnetProgramCompiler.Compile(BuildFixture(), Seed.ToString()), theme, _tuning)
        {
            Metadata = new("商籁 / SONNET", "AvaloniaSilkEffects", "Folia v0.7.2 parity fixture"),
        };
    }

    public override string Name => _scene.ActiveShotKind is { } kind ? $"Sonnet · {kind}" : "Sonnet v0.7.2";

    public override void Initialize(EffectDevice device)
    {
        base.Initialize(device);
        _scene.Initialize(device);
    }

    public override void Resize(PixelSize pixelSize, double renderScaling)
    {
        base.Resize(pixelSize, renderScaling);
        _scene.Resize(pixelSize, renderScaling);
    }

    public override void Update(in EffectFrame frame)
    {
        base.Update(frame);
        _tuning.CameraIntensity = 0.5f + Intensity;
        _tuning.TypographyMotion = 0.5f + Intensity;
        _tuning.TextureResolution = TextRasterScale;
        _scene.Theme = _scene.Theme with { Accent = Accent };
        _scene.Audio = new(Beat, Beat * 0.8f, MathF.Abs(MathF.Sin(Time * 1.3f)));
        _scene.Update(frame);
    }

    public override void Render(EffectRenderContext context) => _scene.Render(context);
    public override void DisposeGpuResources() => _scene.DisposeGpuResources();

    private static IReadOnlyList<SonnetLine> BuildFixture()
    {
        var texts = new[]
        {
            "薄明かりに 名前を置いて", "短い線が 静かにほどける", "世界， 再见！", "It's time, time.",
            "あなたへ 届くまで", "一页一页 翻动的诗稿", "光は輪郭だけを残す", "WE BREATHE IN TYPE",
            "远处的星 沿轨迹坠落", "言葉がカメラを連れていく", "破片と余白のあいだ", "CHORUS / 再一次靠近",
            "巨大な文字が 開いて", "RGB の影を引きずる", "静かな場面にも 呼吸がある", "間奏 / instrumental break",
            "線と円と フレーム", "海のように 揺れる図形", "POSTER BLOCKS", "縦書きと横組み",
            "最后一段 慢慢退场", "credits are waiting", "商籁", "SONNET",
        };
        var output = new List<SonnetLine>();
        for (var index = 0; index < texts.Length; index++)
        {
            var start = index * 2.35;
            var words = texts[index].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var timings = words.Select((word, wordIndex) => new SonnetWordTiming(
                word,
                start + wordIndex * 1.7 / Math.Max(1, words.Length),
                start + (wordIndex + 1) * 1.7 / Math.Max(1, words.Length))).ToArray();
            output.Add(new(texts[index], start, start + 1.85, timings,
                SongPart: index is >= 11 and <= 14 ? "chorus" : index == 15 ? "break" : "verse",
                BlockIndex: index / 4,
                IsChorus: index is >= 11 and <= 14,
                RenderEndTime: start + 2.05));
        }
        return output;
    }
}
