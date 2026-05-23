namespace NeteaseCloudMusic.AudioMatch;

/// <summary>
/// Generates the NetEase Cloud Music audio fingerprint used by the audio match endpoint.
/// </summary>
public interface INeteaseAudioFingerprinter
{
    /// <summary>
    /// Generates a base64 encoded fingerprint from mono Float32 PCM samples.
    /// </summary>
    /// <param name="samples">Mono Float32 PCM samples. The upstream recognizer expects 8 kHz audio.</param>
    /// <param name="cancellationToken">A token used to cancel the fingerprint process.</param>
    /// <returns>The base64 encoded <c>audioFP</c> value accepted by NetEase Cloud Music.</returns>
    Task<string> GenerateFingerprintBase64Async(
        ReadOnlyMemory<float> samples,
        CancellationToken cancellationToken = default);
}
