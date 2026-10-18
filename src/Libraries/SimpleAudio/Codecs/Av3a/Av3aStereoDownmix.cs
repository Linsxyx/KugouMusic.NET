namespace SimpleAudio.Codecs.Av3a;

internal sealed class Av3aStereoDownmix
{
    private readonly float[] _left;
    private readonly float[] _right;

    public Av3aStereoDownmix(int channels, uint channelConfig)
    {
        _left = new float[channels];
        _right = new float[channels];
        if (channels == 1)
        {
            _left[0] = _right[0] = 1;
            return;
        }

        _left[0] = _right[1] = 1;
        if (channels == 2)
            return;

        if (channelConfig == 6 && channels == 4) // 4.0: L R Ls Rs, no center/LFE.
        {
            _left[2] = _right[3] = MathF.Sqrt(0.5f);
        }
        else
        {
            _left[2] = _right[2] = MathF.Sqrt(0.5f);
            // Native channel order: L R C LFE, followed by left/right pairs.
            // LFE is omitted from the stereo mix. Height pairs use lower gain.
            var bedChannels = channelConfig is 3 or 9 or 10 ? 8 : 6;
            for (var channel = 4; channel < channels; channel++)
            {
                var gain = channel < bedChannels ? MathF.Sqrt(0.5f) : 0.5f;
                if (channel % 2 == 0)
                    _left[channel] = gain;
                else
                    _right[channel] = gain;
            }
        }

        // Keep even fully correlated full-scale channels within [-1, 1].
        var scale = 1f / MathF.Max(_left.Sum(), _right.Sum());
        for (var channel = 0; channel < channels; channel++)
        {
            _left[channel] *= scale;
            _right[channel] *= scale;
        }
    }

    public void Convert(ReadOnlySpan<short> pcm, Span<float> stereo)
    {
        var channels = _left.Length;
        if (pcm.Length % channels != 0 || stereo.Length != pcm.Length / channels * 2)
            throw new ArgumentException("PCM 下混缓冲区尺寸不匹配。");
        for (int input = 0, output = 0; input < pcm.Length; input += channels, output += 2)
        {
            float left = 0, right = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                var value = pcm[input + channel] / 32768f;
                left += value * _left[channel];
                right += value * _right[channel];
            }
            stereo[output] = left;
            stereo[output + 1] = right;
        }
    }
}
