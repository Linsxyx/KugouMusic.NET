namespace AvaloniaSilkEffects.Lumiere.Rigs;

/// <summary>Folia lumiere/catalog.ts: 10 families × 10 profiles, in the design document's family order.</summary>
public static class LumiereCatalog
{
    public static IReadOnlyDictionary<string, string> FamilyLabels { get; } = new Dictionary<string, string>
    {
        ["zenith"] = "天光",
        ["lattice"] = "窗隙",
        ["prism"] = "棱镜",
        ["caustic"] = "焦散",
        ["optics"] = "光路",
        ["wave"] = "衍射",
        ["botany"] = "叶脉",
        ["astral"] = "星象",
        ["stage"] = "追光",
        ["motes"] = "萤尘",
    };

    public static IReadOnlyList<LumiereProfile> Profiles { get; } =
    [
        .. LumiereZenithRigs.All,
        .. LumiereLatticeRigs.All,
        .. LumierePrismRigs.All,
        .. LumiereCausticRigs.All,
        .. LumiereOpticsRigs.All,
        .. LumiereWaveRigs.All,
        .. LumiereBotanyRigs.All,
        .. LumiereAstralRigs.All,
        .. LumiereStageRigs.All,
        .. LumiereMotesRigs.All,
    ];

    private static readonly Dictionary<string, LumiereProfile> ByKind = Profiles.ToDictionary(profile => profile.Kind);

    public static IReadOnlyList<string> Kinds { get; } = [.. Profiles.Select(profile => profile.Kind)];

    /// <summary>Unknown kinds fall back to 天井.</summary>
    public static LumiereProfile ProfileOf(string kind) => ByKind.GetValueOrDefault(kind) ?? Profiles[0];

    public static bool HasProfile(string kind) => ByKind.ContainsKey(kind);
}
