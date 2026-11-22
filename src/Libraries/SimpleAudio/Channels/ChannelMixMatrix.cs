namespace SimpleAudio;

// 通用多声道声学混音矩阵，支持任意源声道拓扑向目标设备声道拓扑的能量归一化折叠。
internal sealed class ChannelMixMatrix
{
    private const float CenterGain = 0.70710678f;   // -3 dB (sqrt(0.5))
    private const float SurroundGain = 0.70710678f; // -3 dB
    private const float HeightGain = 0.5f;          // -6 dB
    private const float Int16Scale = 1f / 32768f;

    // 行主序存储：[outCh * SourceChannels + inCh]
    private readonly float[] _floatWeights;
    private readonly float[] _int16Weights;

    public int SourceChannels { get; }
    public int TargetChannels { get; }
    public bool IsIdentity { get; }

    private ChannelMixMatrix(int sourceChannels, int targetChannels, float[] floatWeights, bool isIdentity)
    {
        SourceChannels = sourceChannels;
        TargetChannels = targetChannels;
        _floatWeights = floatWeights;
        IsIdentity = isIdentity;

        _int16Weights = new float[floatWeights.Length];
        for (var i = 0; i < floatWeights.Length; i++)
        {
            _int16Weights[i] = floatWeights[i] * Int16Scale;
        }
    }

    public float GetWeight(int targetChannel, int sourceChannel) =>
        _floatWeights[targetChannel * SourceChannels + sourceChannel];

    // 根据源布局与目标布局自动构建声学混音矩阵，并按能量（RMS）归一化防止音量塌陷。
    public static ChannelMixMatrix Build(AudioChannelLayout source, AudioChannelLayout target)
    {
        var srcCount = source.Channels;
        var dstCount = target.Channels;
        var weights = new float[dstCount * srcCount];

        if (source.HasSameRoleOrder(target))
        {
            for (var i = 0; i < dstCount; i++)
            {
                weights[i * srcCount + i] = 1f;
            }

            return new ChannelMixMatrix(srcCount, dstCount, weights, isIdentity: true);
        }

        // 建立目标声道角色索引，便于精确路由。
        var dstIndex = new Dictionary<AudioChannelRole, int>(dstCount);
        for (var d = 0; d < dstCount; d++)
        {
            dstIndex.TryAdd(target.Roles[d], d);
        }

        var hasFl = dstIndex.TryGetValue(AudioChannelRole.FrontLeft, out var fl);
        var hasFr = dstIndex.TryGetValue(AudioChannelRole.FrontRight, out var fr);
        var hasFc = dstIndex.TryGetValue(AudioChannelRole.FrontCenter, out var fc);
        var hasBl = dstIndex.TryGetValue(AudioChannelRole.BackLeft, out var bl);
        var hasBr = dstIndex.TryGetValue(AudioChannelRole.BackRight, out var br);
        var hasSl = dstIndex.TryGetValue(AudioChannelRole.SideLeft, out var sl);
        var hasSr = dstIndex.TryGetValue(AudioChannelRole.SideRight, out var sr);

        for (var s = 0; s < srcCount; s++)
        {
            var role = source.Roles[s];

            // 1. 目标设备包含完全一致的物理声道角色时直接 1:1 路由。
            if (dstIndex.TryGetValue(role, out var exactDst))
            {
                weights[exactDst * srcCount + s] += 1f;
                continue;
            }

            // 2. 目标缺少对应声道时，按声学方位折叠到最邻近扬声器。
            switch (role)
            {
                case AudioChannelRole.FrontCenter:
                    if (hasFl && hasFr)
                    {
                        var gain = srcCount == 1 ? 1f : CenterGain;
                        weights[fl * srcCount + s] += gain;
                        weights[fr * srcCount + s] += gain;
                    }
                    else if (dstCount > 0)
                    {
                        weights[s % dstCount] += 1f;
                    }
                    break;

                case AudioChannelRole.Lfe:
                    // ITU-R BS.775 建议下混到无独立低音炮拓扑（如 2.0 / 4.0）时省略 LFE，避免低频过载浑浊。
                    break;

                case AudioChannelRole.SideLeft:
                    if (hasBl)
                        weights[bl * srcCount + s] += 1f;
                    else if (hasFl)
                        weights[fl * srcCount + s] += SurroundGain;
                    break;

                case AudioChannelRole.SideRight:
                    if (hasBr)
                        weights[br * srcCount + s] += 1f;
                    else if (hasFr)
                        weights[fr * srcCount + s] += SurroundGain;
                    break;

                case AudioChannelRole.BackLeft:
                    if (hasSl)
                        weights[sl * srcCount + s] += 1f;
                    else if (hasFl)
                        weights[fl * srcCount + s] += SurroundGain;
                    break;

                case AudioChannelRole.BackRight:
                    if (hasSr)
                        weights[sr * srcCount + s] += 1f;
                    else if (hasFr)
                        weights[fr * srcCount + s] += SurroundGain;
                    break;

                case AudioChannelRole.TopFrontLeft:
                    if (hasFl)
                        weights[fl * srcCount + s] += HeightGain;
                    break;

                case AudioChannelRole.TopFrontRight:
                    if (hasFr)
                        weights[fr * srcCount + s] += HeightGain;
                    break;

                case AudioChannelRole.TopBackLeft:
                    if (hasBl)
                        weights[bl * srcCount + s] += HeightGain;
                    else if (hasSl)
                        weights[sl * srcCount + s] += HeightGain;
                    else if (hasFl)
                        weights[fl * srcCount + s] += HeightGain;
                    break;

                case AudioChannelRole.TopBackRight:
                    if (hasBr)
                        weights[br * srcCount + s] += HeightGain;
                    else if (hasSr)
                        weights[sr * srcCount + s] += HeightGain;
                    else if (hasFr)
                        weights[fr * srcCount + s] += HeightGain;
                    break;

                case AudioChannelRole.FrontLeft:
                    if (hasFc)
                        weights[fc * srcCount + s] += CenterGain;
                    break;

                case AudioChannelRole.FrontRight:
                    if (hasFc)
                        weights[fc * srcCount + s] += CenterGain;
                    break;

                default:
                    if (hasFl && hasFr)
                    {
                        var sideDst = s % 2 == 0 ? fl : fr;
                        weights[sideDst * srcCount + s] += HeightGain;
                    }
                    break;
            }
        }

        // 3. 按输出声道独立计算功率（RMS）归一化系数，保持与普通立体声一致的听感响度，
        // 同时让未发生折叠的直通声道（如 7.1.4 -> 5.1 中的 C/LFE）保持 1.0 增益。
        // 极端全相干瞬态峰值由 SimpleAudioPlayer 尾端的 OutputLimiterDSP 软限幅保护。
        for (var d = 0; d < dstCount; d++)
        {
            var rowOffset = d * srcCount;
            var energySum = 0f;
            for (var s = 0; s < srcCount; s++)
            {
                var w = weights[rowOffset + s];
                energySum += w * w;
            }

            if (energySum > 1.0001f)
            {
                var norm = 1f / MathF.Sqrt(energySum);
                for (var s = 0; s < srcCount; s++)
                {
                    weights[rowOffset + s] *= norm;
                }
            }
        }

        return new ChannelMixMatrix(srcCount, dstCount, weights, isIdentity: false);
    }

    // 将交错 Int16 PCM 按矩阵转换为目标声道交错 Float32 PCM（已预合并 1/32768 缩放）。
    public void ConvertInt16(ReadOnlySpan<short> input, Span<float> output)
    {
        var srcCh = SourceChannels;
        var dstCh = TargetChannels;
        if (input.Length % srcCh != 0)
            throw new ArgumentException("输入 Int16 PCM 长度必须是源声道数的整数倍。", nameof(input));

        var frames = input.Length / srcCh;
        if (output.Length < frames * dstCh)
            throw new ArgumentException("输出 Float32 PCM 缓冲区长度不足。", nameof(output));

        if (IsIdentity)
        {
            for (var i = 0; i < input.Length; i++)
            {
                output[i] = input[i] * Int16Scale;
            }

            return;
        }

        var w = _int16Weights;
        if (dstCh == 2)
        {
            var row1 = srcCh;
            for (int f = 0, inIdx = 0, outIdx = 0; f < frames; f++, inIdx += srcCh, outIdx += 2)
            {
                float left = 0f, right = 0f;
                for (var s = 0; s < srcCh; s++)
                {
                    var sample = input[inIdx + s];
                    left += sample * w[s];
                    right += sample * w[row1 + s];
                }

                output[outIdx] = left;
                output[outIdx + 1] = right;
            }

            return;
        }

        for (int f = 0, inIdx = 0, outIdx = 0; f < frames; f++, inIdx += srcCh, outIdx += dstCh)
        {
            for (var d = 0; d < dstCh; d++)
            {
                var rowOffset = d * srcCh;
                var sum = 0f;
                for (var s = 0; s < srcCh; s++)
                {
                    sum += input[inIdx + s] * w[rowOffset + s];
                }

                output[outIdx + d] = sum;
            }
        }
    }

    // 将交错 Float32 PCM 按矩阵混音为目标声道交错 Float32 PCM。
    public void ConvertFloat(ReadOnlySpan<float> input, Span<float> output)
    {
        var srcCh = SourceChannels;
        var dstCh = TargetChannels;
        if (input.Length % srcCh != 0)
            throw new ArgumentException("输入 Float32 PCM 长度必须是源声道数的整数倍。", nameof(input));

        var frames = input.Length / srcCh;
        if (output.Length < frames * dstCh)
            throw new ArgumentException("输出 Float32 PCM 缓冲区长度不足。", nameof(output));

        if (IsIdentity)
        {
            input.CopyTo(output);
            return;
        }

        var w = _floatWeights;
        if (dstCh == 2)
        {
            var row1 = srcCh;
            for (int f = 0, inIdx = 0, outIdx = 0; f < frames; f++, inIdx += srcCh, outIdx += 2)
            {
                float left = 0f, right = 0f;
                for (var s = 0; s < srcCh; s++)
                {
                    var sample = input[inIdx + s];
                    left += sample * w[s];
                    right += sample * w[row1 + s];
                }

                output[outIdx] = left;
                output[outIdx + 1] = right;
            }

            return;
        }

        for (int f = 0, inIdx = 0, outIdx = 0; f < frames; f++, inIdx += srcCh, outIdx += dstCh)
        {
            for (var d = 0; d < dstCh; d++)
            {
                var rowOffset = d * srcCh;
                var sum = 0f;
                for (var s = 0; s < srcCh; s++)
                {
                    sum += input[inIdx + s] * w[rowOffset + s];
                }

                output[outIdx + d] = sum;
            }
        }
    }
}
