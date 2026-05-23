namespace NeteaseCloudMusic.AudioMatch;

/// <summary>
/// Runtime options for the bundled ncm-afp fingerprint bridge.
/// </summary>
public sealed record NeteaseAudioFingerprintOptions
{
    /// <summary>
    /// The executable used to run the bundled ncm-afp JavaScript/WASM bridge.
    /// </summary>
    public string NodeExecutable { get; init; } = "node";

    /// <summary>
    /// The maximum time allowed for one fingerprint generation.
    /// </summary>
    public TimeSpan ProcessTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Optional directory containing <c>runner.js</c>, <c>afp.js</c>, and <c>afp.wasm.js</c>.
    /// </summary>
    public string? RuntimeDirectory { get; init; }
}
