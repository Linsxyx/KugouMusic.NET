using System.Buffers.Binary;
using System.Text.Json;
using KgWebApi.Net.Models;
using NeteaseCloudMusic.AudioMatch;

namespace KgWebApi.Net.Services;

public sealed class NeteaseAudioMatchService(
    IHttpClientFactory httpClientFactory,
    INeteaseAudioFingerprinter fingerprinter)
{
    private const string SessionId = "0123456789abcdef";
    private const string AlgorithmCode = "shazam_v2";

    public async Task<NeteaseAudioMatchResponse> MatchFingerprintAsync(
        int duration,
        string audioFP,
        CancellationToken cancellationToken = default)
    {
        if (duration <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "音频时长必须大于 0 秒");
        }

        if (string.IsNullOrWhiteSpace(audioFP))
        {
            throw new ArgumentException("音频指纹不能为空", nameof(audioFP));
        }

        var query = new Dictionary<string, string?>
        {
            ["sessionId"] = SessionId,
            ["algorithmCode"] = AlgorithmCode,
            ["duration"] = duration.ToString(),
            ["rawdata"] = audioFP,
            ["times"] = "1",
            ["decrypt"] = "1"
        };

        var requestUri = QueryString.Create(query).ToUriComponent();
        var client = httpClientFactory.CreateClient(WebApiKgHttpClientNames.NeteaseCloudMusic);
        using var response = await client.GetAsync($"/api/music/audio/match{requestUri}", cancellationToken)
            .ConfigureAwait(false);

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var data = root.TryGetProperty("data", out var dataElement)
            ? dataElement.Clone()
            : root.Clone();

        return new NeteaseAudioMatchResponse(200, data);
    }

    public async Task<NeteaseAudioMatchResponse> MatchPcmAsync(
        Stream pcmStream,
        int? duration,
        CancellationToken cancellationToken = default)
    {
        var bytes = await ReadAllBytesAsync(pcmStream, cancellationToken).ConfigureAwait(false);
        var samples = DecodeFloat32Pcm(bytes);
        var resolvedDuration = duration.GetValueOrDefault(
            Math.Max(1, (int)Math.Ceiling(samples.Length / (double)NeteaseAudioFingerprinter.ExpectedSampleRate)));

        var audioFP = await fingerprinter.GenerateFingerprintBase64Async(samples, cancellationToken).ConfigureAwait(false);
        var match = await MatchFingerprintAsync(resolvedDuration, audioFP, cancellationToken).ConfigureAwait(false);
        return match with { AudioFP = audioFP };
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    private static float[] DecodeFloat32Pcm(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            throw new ArgumentException("PCM 数据不能为空");
        }

        if (bytes.Length % sizeof(float) != 0)
        {
            throw new ArgumentException("PCM 数据必须是 little-endian Float32");
        }

        var samples = new float[bytes.Length / sizeof(float)];
        for (var i = 0; i < samples.Length; i++)
        {
            var bits = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float)));
            samples[i] = BitConverter.Int32BitsToSingle(bits);
        }

        return samples;
    }
}
