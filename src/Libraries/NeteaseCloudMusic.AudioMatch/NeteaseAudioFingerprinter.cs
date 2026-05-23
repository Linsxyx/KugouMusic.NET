using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace NeteaseCloudMusic.AudioMatch;

/// <summary>
/// Generates NetEase Cloud Music audio fingerprints by invoking the bundled ncm-afp WASM runtime.
/// </summary>
public sealed class NeteaseAudioFingerprinter : INeteaseAudioFingerprinter
{
    /// <summary>
    /// The sample rate expected by the bundled ncm-afp fingerprint algorithm.
    /// </summary>
    public const int ExpectedSampleRate = 8000;

    private readonly NeteaseAudioFingerprintOptions _options;

    /// <summary>
    /// Creates a fingerprinter using the default runtime options.
    /// </summary>
    public NeteaseAudioFingerprinter()
        : this(new NeteaseAudioFingerprintOptions())
    {
    }

    /// <summary>
    /// Creates a fingerprinter using custom runtime options.
    /// </summary>
    /// <param name="options">Runtime options for invoking the bundled ncm-afp bridge.</param>
    public NeteaseAudioFingerprinter(NeteaseAudioFingerprintOptions options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public async Task<string> GenerateFingerprintBase64Async(
        ReadOnlyMemory<float> samples,
        CancellationToken cancellationToken = default)
    {
        if (samples.IsEmpty)
        {
            throw new ArgumentException("Audio samples cannot be empty.", nameof(samples));
        }

        var runtimeDirectory = ResolveRuntimeDirectory();
        var runnerPath = Path.Combine(runtimeDirectory, "runner.js");
        if (!File.Exists(runnerPath))
        {
            throw new FileNotFoundException("The ncm-afp runner was not found.", runnerPath);
        }

        var timeout = _options.ProcessTimeout <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(30)
            : _options.ProcessTimeout;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = _options.NodeExecutable,
            ArgumentList = { runnerPath },
            WorkingDirectory = runtimeDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start the ncm-afp fingerprint process.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Failed to start '{_options.NodeExecutable}'. Install Node.js or set {nameof(NeteaseAudioFingerprintOptions.NodeExecutable)}.",
                ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutSource.Token);

        try
        {
            var inputBytes = EncodeSamples(samples.Span);
            await process.StandardInput.BaseStream.WriteAsync(inputBytes, timeoutSource.Token).ConfigureAwait(false);
            process.StandardInput.Close();

            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"ncm-afp fingerprint process exited with code {process.ExitCode}: {stderr.Trim()}");
            }

            return ParseFingerprint(stdout, stderr);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"ncm-afp fingerprint generation exceeded {timeout.TotalSeconds:0.#} seconds.");
        }
        catch
        {
            TryKill(process);
            throw;
        }
    }

    private string ResolveRuntimeDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_options.RuntimeDirectory))
        {
            return _options.RuntimeDirectory;
        }

        return Path.Combine(AppContext.BaseDirectory, "ncm-afp");
    }

    private static byte[] EncodeSamples(ReadOnlySpan<float> samples)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        if (BitConverter.IsLittleEndian)
        {
            MemoryMarshal.AsBytes(samples).CopyTo(bytes);
            return bytes;
        }

        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(i * sizeof(float), sizeof(float)),
                BitConverter.SingleToInt32Bits(samples[i]));
        }

        return bytes;
    }

    private static string ParseFingerprint(string stdout, string stderr)
    {
        try
        {
            var response = JsonSerializer.Deserialize<FingerprintRunnerResponse>(
                stdout,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (response is { Ok: true } && !string.IsNullOrWhiteSpace(response.Fingerprint))
            {
                return response.Fingerprint;
            }

            throw new InvalidOperationException(response?.Error ?? "ncm-afp returned an empty fingerprint.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"ncm-afp returned malformed output. stdout='{stdout.Trim()}', stderr='{stderr.Trim()}'.",
                ex);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private sealed record FingerprintRunnerResponse(
        bool Ok,
        string? Fingerprint,
        string? Error);
}
