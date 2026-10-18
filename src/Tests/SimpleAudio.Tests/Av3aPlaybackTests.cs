using System.Diagnostics;
using ManagedBass;
using ManagedBass.Fx;
using SimpleAudio.Codecs.Av3a;
using Xunit;

namespace SimpleAudio.Tests;

[CollectionDefinition("BASS", DisableParallelization = true)]
public class BassCollection;

internal sealed class Av3aSampleFactAttribute : FactAttribute
{
    public Av3aSampleFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("SIMPLEAUDIO_AV3A_SAMPLE")))
            Skip = "Set SIMPLEAUDIO_AV3A_SAMPLE to a local AV3A M4A file to run native integration checks.";
    }
}

[Collection("BASS")]
public class Av3aPlaybackTests
{
    private static string SamplePath => Environment.GetEnvironmentVariable("SIMPLEAUDIO_AV3A_SAMPLE")!;

    [Av3aSampleFact]
    public void M4a_CanDecodeSeekReplayAndPassThroughBassFx()
    {
        Assert.True(Bass.Init(0), Bass.LastError.ToString()); // null device: no audible output
        var handle = 0;
        try
        {
            using var input = File.OpenRead(SamplePath);
            var track = Av3aMp4Track.TryRead(input);
            Assert.NotNull(track);
            var firstPacket = new byte[track.Samples[0].Size];
            track.ReadPacket(input, 0, firstPacket);
            using var decoder = new Av3aNativeDecoder(firstPacket);
            var expectedDuration = track.Samples.Length * 1024.0 / decoder.Info.SampleRate;
            var stopwatch = Stopwatch.StartNew();
            handle = AudioSourceStreamFactory.Create(SamplePath, BassFlags.Decode | BassFlags.Float);
            Assert.True(handle != 0, AudioSourceStreamFactory.LastError ?? Bass.LastError.ToString());
            var startupSeconds = stopwatch.Elapsed.TotalSeconds;
            Assert.True(startupSeconds < 5, $"BASS initialization blocked for {startupSeconds:F3}s instead of starting progressively.");
            Assert.True(Bass.ChannelGetInfo(handle, out var info));
            Assert.Equal(2, info.Channels);
            Assert.Equal((int)decoder.Info.SampleRate, info.Frequency);
            Assert.InRange(Bass.ChannelBytes2Seconds(handle, Bass.ChannelGetLength(handle)), expectedDuration - 0.001, expectedDuration + 0.001);
            var head = new float[4096];
            Assert.Equal(head.Length * 4, Bass.ChannelGetData(handle, head, head.Length * 4));
            Assert.Contains(head, sample => Math.Abs(sample) > 0.000001f);
            var nearEnd = Math.Max(0, expectedDuration - 1);
            Assert.True(Bass.ChannelSetPosition(handle, Bass.ChannelSeconds2Bytes(handle, nearEnd)));
            var tail = new float[4096];
            Assert.Equal(tail.Length * 4, Bass.ChannelGetData(handle, tail, tail.Length * 4));
            Assert.True(Bass.ChannelSetPosition(handle, 0));
            var replay = new float[head.Length];
            Assert.Equal(replay.Length * 4, Bass.ChannelGetData(handle, replay, replay.Length * 4));
            Assert.Equal(head, replay);
            Assert.True(Bass.ChannelSetPosition(handle, 0));
            var tempo = BassFx.TempoCreate(handle, BassFlags.Decode | BassFlags.Float | BassFlags.FxFreeSource);
            Assert.True(tempo != 0, Bass.LastError.ToString());
            handle = tempo;
            Assert.True(Bass.ChannelSetAttribute(handle, ChannelAttribute.Tempo, 15));
            Assert.True(Bass.ChannelGetData(handle, replay, replay.Length * 4) > 0);
            Assert.True(Bass.ChannelSetPosition(handle, Bass.ChannelSeconds2Bytes(handle, 30)));
            Assert.True(Bass.ChannelGetData(handle, replay, replay.Length * 4) > 0);
            Console.WriteLine($"AV3A startup={startupSeconds:F3}s, duration={expectedDuration:F3}s, complete cache={stopwatch.Elapsed.TotalSeconds:F3}s");
        }
        finally
        {
            if (handle != 0)
                Bass.StreamFree(handle);
            Bass.Free();
        }
    }

    [Av3aSampleFact]
    public void NonseekableHttpBody_LoadsAndReleasesItsOwnerOnce()
    {
        Assert.True(Bass.Init(0));
        var owner = new CountingOwner();
        var input = new SeekableReadCacheTests.FragmentedStream(File.ReadAllBytes(SamplePath));
        var handle = 0;
        try
        {
            handle = AudioSourceStreamFactory.Create(input, input.Length, BassFlags.Decode | BassFlags.Float, owner);
            Assert.True(handle != 0, AudioSourceStreamFactory.LastError ?? Bass.LastError.ToString());
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var pcm = new float[4096];
            Assert.True(Bass.ChannelGetData(handle, pcm, pcm.Length * 4) > 0);
            AudioSourceStreamFactory.CancelPendingReads(handle);
            Assert.True(Bass.StreamFree(handle));
            handle = 0;
            Assert.Equal(1, owner.Disposals);
            Assert.False(input.CanRead);
        }
        finally
        {
            if (handle != 0)
                Bass.StreamFree(handle);
            Bass.Free();
        }
    }

    [Av3aSampleFact]
    public void CorruptedCrc_IsRejectedBeforePlayback()
    {
        using var input = File.OpenRead(SamplePath);
        var track = Av3aMp4Track.TryRead(input)!;
        var packet = new byte[track.Samples[0].Size];
        track.ReadPacket(input, 0, packet);
        using var decoder = new Av3aNativeDecoder(packet);
        packet[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => decoder.Decode(packet));
    }

    private sealed class CountingOwner : IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }
}
