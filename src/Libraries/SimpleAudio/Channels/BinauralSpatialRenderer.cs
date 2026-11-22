namespace SimpleAudio;

// 耳机双耳 3D 虚拟环绕渲染器（Binaural / 简化结构化 HRTF）：
// 结合 Woodworth 双耳时间差（ITD）、Brown-Duda 头影高频衍射滤波、前后/仰角耳廓 EQ 与轻量早期房间反射，
// 将 4.0 / 5.1 / 7.1 / 7.1.4 等多声道音频在普通双声道耳机上渲染出真实的方位层次与声场纵深。
internal sealed class BinauralSpatialRenderer : IAudioChannelRenderer
{
    private const float Int16Scale = 1f / 32768f;
    private const float HeadRadiusMeters = 0.0875f;
    private const float SpeedOfSound = 343f;
    private const int DelayLineSize = 128; // 覆盖最大 ~2.6ms ITD（48kHz 下仅需 ~32 samples）
    private const int DelayLineMask = DelayLineSize - 1;

    private readonly ChannelProcessor[] _processors;
    private readonly EarlyReflectionRoom _room;
    private readonly float _outputGain;

    public int SourceChannels { get; }
    public int TargetChannels => 2;

    public BinauralSpatialRenderer(AudioChannelLayout sourceLayout, int sampleRate)
    {
        SourceChannels = sourceLayout.Channels;
        var safeRate = sampleRate > 0 ? sampleRate : 48000;
        _processors = new ChannelProcessor[SourceChannels];
        _room = new EarlyReflectionRoom(safeRate);

        double energySumL = 0;
        double energySumR = 0;

        for (var ch = 0; ch < SourceChannels; ch++)
        {
            var role = sourceLayout.Roles[ch];
            var (azDeg, elDeg, weight, roomSend, isLfe) = ResolveSpeakerGeometry(role, ch, SourceChannels, sourceLayout.IsDiscrete);
            var processor = new ChannelProcessor(safeRate, azDeg, elDeg, weight, roomSend, isLfe);
            _processors[ch] = processor;

            energySumL += processor.NominalLeftEnergy;
            energySumR += processor.NominalRightEnergy;
        }

        // 功率归一化：保证多声道双耳渲染与普通立体声保持相近响度，避免过小声或持续过载。
        var maxEnergy = Math.Max(1.0, Math.Max(energySumL, energySumR));
        _outputGain = (float)(1.0 / Math.Sqrt(maxEnergy));
    }

    public void RenderInt16(ReadOnlySpan<short> input, Span<float> output)
    {
        var srcCh = SourceChannels;
        if (input.Length % srcCh != 0)
            throw new ArgumentException("输入 Int16 PCM 长度必须是源声道数的整数倍。", nameof(input));

        var frames = input.Length / srcCh;
        if (output.Length < frames * 2)
            throw new ArgumentException("输出双耳 Float32 缓冲区长度不足。", nameof(output));

        var gain = _outputGain;
        for (int f = 0, inIdx = 0, outIdx = 0; f < frames; f++, inIdx += srcCh, outIdx += 2)
        {
            float directL = 0f, directR = 0f;
            float roomInL = 0f, roomInR = 0f;

            for (var c = 0; c < srcCh; c++)
            {
                var sample = input[inIdx + c] * Int16Scale;
                _processors[c].ProcessSample(sample, ref directL, ref directR, ref roomInL, ref roomInR);
            }

            _room.ProcessSample(roomInL, roomInR, out var refL, out var refR);
            output[outIdx] = (directL + refL) * gain;
            output[outIdx + 1] = (directR + refR) * gain;
        }
    }

    public void RenderFloat(ReadOnlySpan<float> input, Span<float> output)
    {
        var srcCh = SourceChannels;
        if (input.Length % srcCh != 0)
            throw new ArgumentException("输入 Float32 PCM 长度必须是源声道数的整数倍。", nameof(input));

        var frames = input.Length / srcCh;
        if (output.Length < frames * 2)
            throw new ArgumentException("输出双耳 Float32 缓冲区长度不足。", nameof(output));

        var gain = _outputGain;
        for (int f = 0, inIdx = 0, outIdx = 0; f < frames; f++, inIdx += srcCh, outIdx += 2)
        {
            float directL = 0f, directR = 0f;
            float roomInL = 0f, roomInR = 0f;

            for (var c = 0; c < srcCh; c++)
            {
                _processors[c].ProcessSample(input[inIdx + c], ref directL, ref directR, ref roomInL, ref roomInR);
            }

            _room.ProcessSample(roomInL, roomInR, out var refL, out var refR);
            output[outIdx] = (directL + refL) * gain;
            output[outIdx + 1] = (directR + refR) * gain;
        }
    }

    public void Reset()
    {
        foreach (var processor in _processors)
        {
            processor.Reset();
        }

        _room.Reset();
    }

    // 返回虚拟扬声器的球面方位角（左负右正，±180°为正后方）、仰角、声道权重与早期反射发送量。
    private static (float AzimuthDeg, float ElevationDeg, float Weight, float RoomSend, bool IsLfe) ResolveSpeakerGeometry(
        AudioChannelRole role,
        int index,
        int totalChannels,
        bool isDiscrete)
    {
        if (isDiscrete)
        {
            // 离散多轨（如 TDM8）：将各立体声对沿前半球错开摆位（±30°, ±55°, ±80°, ±110°），保留全部音轨且拉开层次。
            var pairIndex = index / 2;
            var isRight = (index & 1) != 0;
            var angle = Math.Min(120f, 30f + pairIndex * 28f);
            return (isRight ? angle : -angle, 0f, 0.90f, 0.14f, false);
        }

        return role switch
        {
            AudioChannelRole.FrontLeft => (-30f, 0f, 1.00f, 0.12f, false),
            AudioChannelRole.FrontRight => (30f, 0f, 1.00f, 0.12f, false),
            AudioChannelRole.FrontCenter => (0f, 0f, 0.85f, 0.10f, false),
            AudioChannelRole.Lfe => (0f, -15f, 0.42f, 0.0f, true),
            AudioChannelRole.SideLeft => (-95f, 0f, 0.92f, 0.22f, false),
            AudioChannelRole.SideRight => (95f, 0f, 0.92f, 0.22f, false),
            AudioChannelRole.BackLeft => (-135f, 0f, 0.90f, 0.28f, false),
            AudioChannelRole.BackRight => (135f, 0f, 0.90f, 0.28f, false),
            AudioChannelRole.TopFrontLeft => (-45f, 45f, 0.78f, 0.24f, false),
            AudioChannelRole.TopFrontRight => (45f, 45f, 0.78f, 0.24f, false),
            AudioChannelRole.TopBackLeft => (-135f, 45f, 0.75f, 0.30f, false),
            AudioChannelRole.TopBackRight => (135f, 45f, 0.75f, 0.30f, false),
            _ => (index % 2 == 0 ? -60f : 60f, 0f, 0.75f, 0.16f, false)
        };
    }

    // 单虚拟声道双耳处理器：耳廓滤波 + 双耳独立延迟（ITD）+ 对侧耳头影滤波（ILD + 衍射）。
    private sealed class ChannelProcessor
    {
        private readonly float[] _delay = new float[DelayLineSize];
        private readonly float _delayLeftSamples;
        private readonly float _delayRightSamples;
        private readonly float _gainLeft;
        private readonly float _gainRight;
        private readonly float _roomSend;
        private readonly bool _hasPreFilter1;
        private readonly bool _hasPreFilter2;
        private readonly bool _hasShadowLeft;
        private readonly bool _hasShadowRight;

        private BiquadFilter _preFilter1;
        private BiquadFilter _preFilter2;
        private HeadShadowFilter _shadowLeft;
        private HeadShadowFilter _shadowRight;
        private int _writeIndex;

        public double NominalLeftEnergy { get; }
        public double NominalRightEnergy { get; }

        public ChannelProcessor(int sampleRate, float azimuthDeg, float elevationDeg, float weight, float roomSend, bool isLfe)
        {
            _roomSend = roomSend;

            if (isLfe)
            {
                // LFE：居中无 ITD，温和滚降高频保留低频震撼感与 5.1/7.1 测试音可辨识度。
                _delayLeftSamples = 0f;
                _delayRightSamples = 0f;
                _gainLeft = weight;
                _gainRight = weight;
                _hasPreFilter1 = true;
                _preFilter1 = BiquadFilter.CreateHighShelf(sampleRate, 180f, -8.5f, 0.707f);
                NominalLeftEnergy = weight * weight * 0.5;
                NominalRightEnergy = weight * weight * 0.5;
                return;
            }

            var azRad = azimuthDeg * (MathF.PI / 180f);
            var elRad = elevationDeg * (MathF.PI / 180f);
            var absAz = MathF.Abs(azRad);
            var cosEl = MathF.Cos(elRad);

            // Woodworth 球头 ITD 模型：theta = sin/arc 沿耳轴映射到 [0, pi/2]
            var lateralAngle = MathF.Min(absAz, MathF.PI - absAz) * cosEl;
            var itdSeconds = (HeadRadiusMeters / SpeedOfSound) * (MathF.Sin(lateralAngle) + lateralAngle);
            var itdSamples = Math.Clamp(itdSeconds * sampleRate, 0f, DelayLineSize - 4);

            // 方位角为负（左侧声源）：左耳先到达，右耳延迟 ITD；反之亦然。
            var lateralSin = MathF.Sin(absAz) * cosEl; // [0, 1]：0=正前/正后，1=正侧耳
            if (azimuthDeg < -0.5f)
            {
                _delayLeftSamples = 0f;
                _delayRightSamples = itdSamples;
                _gainLeft = weight * (1.0f + 0.14f * lateralSin);
                _gainRight = weight * (1.0f - 0.52f * lateralSin);
                _hasShadowRight = lateralSin > 0.05f;
                _shadowRight = new HeadShadowFilter(sampleRate, lateralSin);
            }
            else if (azimuthDeg > 0.5f)
            {
                _delayLeftSamples = itdSamples;
                _delayRightSamples = 0f;
                _gainLeft = weight * (1.0f - 0.52f * lateralSin);
                _gainRight = weight * (1.0f + 0.14f * lateralSin);
                _hasShadowLeft = lateralSin > 0.05f;
                _shadowLeft = new HeadShadowFilter(sampleRate, lateralSin);
            }
            else
            {
                // 正前方中置：轻微前置聚焦，双耳对称
                _delayLeftSamples = 0f;
                _delayRightSamples = 0f;
                _gainLeft = weight * 0.70710678f;
                _gainRight = weight * 0.70710678f;
                _hasPreFilter1 = true;
                _preFilter1 = BiquadFilter.CreatePeaking(sampleRate, 3000f, +1.5f, 1.1f);
            }

            // 前后与仰角耳廓频谱线索（Pinna Cues）
            var isRear = absAz > (100f * MathF.PI / 180f);
            var isElevated = elevationDeg > 20f;

            if (isElevated)
            {
                // 天空声道：6 kHz 耳廓凹口 + 9.5 kHz 仰角抬升（Blauert 高度带）
                _hasPreFilter1 = true;
                _preFilter1 = BiquadFilter.CreatePeaking(sampleRate, 6000f, -5.0f, 2.2f);
                _hasPreFilter2 = true;
                _preFilter2 = isRear
                    ? BiquadFilter.CreateHighShelf(sampleRate, 4200f, -3.0f, 0.707f)
                    : BiquadFilter.CreatePeaking(sampleRate, 9500f, +4.2f, 1.8f);
            }
            else if (isRear)
            {
                // 后环绕声道：1.8 kHz 前向临场凹口 + 4.2 kHz 耳廓遮蔽高频滚降
                _hasPreFilter1 = true;
                _preFilter1 = BiquadFilter.CreatePeaking(sampleRate, 1800f, -3.5f, 1.4f);
                _hasPreFilter2 = true;
                _preFilter2 = BiquadFilter.CreateHighShelf(sampleRate, 4200f, -4.5f, 0.707f);
            }

            NominalLeftEnergy = _gainLeft * _gainLeft;
            NominalRightEnergy = _gainRight * _gainRight;
        }

        public void ProcessSample(float sample, ref float directL, ref float directR, ref float roomInL, ref float roomInR)
        {
            var x = sample;
            if (_hasPreFilter1)
                x = _preFilter1.Process(x);
            if (_hasPreFilter2)
                x = _preFilter2.Process(x);

            _delay[_writeIndex] = x;

            var sampleL = ReadDelayed(_delayLeftSamples);
            var sampleR = ReadDelayed(_delayRightSamples);

            if (_hasShadowLeft)
                sampleL = _shadowLeft.Process(sampleL);
            if (_hasShadowRight)
                sampleR = _shadowRight.Process(sampleR);

            var outL = sampleL * _gainLeft;
            var outR = sampleR * _gainRight;

            directL += outL;
            directR += outR;

            if (_roomSend > 0f)
            {
                // 早期反射适度交叉对侧，增强耳机“声场脱离脑壳”的外化听感。
                roomInL += (outL * 0.65f + outR * 0.35f) * _roomSend;
                roomInR += (outR * 0.65f + outL * 0.35f) * _roomSend;
            }

            _writeIndex = (_writeIndex + 1) & DelayLineMask;
        }

        public void Reset()
        {
            Array.Clear(_delay);
            _writeIndex = 0;
            _preFilter1.Reset();
            _preFilter2.Reset();
            _shadowLeft.Reset();
            _shadowRight.Reset();
        }

        private float ReadDelayed(float delaySamples)
        {
            if (delaySamples <= 0.001f)
                return _delay[_writeIndex];

            var intPart = (int)delaySamples;
            var frac = delaySamples - intPart;
            var idx0 = (_writeIndex - intPart) & DelayLineMask;
            var idx1 = (idx0 - 1) & DelayLineMask;
            return _delay[idx0] + (_delay[idx1] - _delay[idx0]) * frac;
        }
    }

    // Brown-Duda 一阶头影高频衰减滤波器：对侧耳保留低频衍射，自然衰减高频。
    private struct HeadShadowFilter
    {
        private readonly float _b0;
        private readonly float _b1;
        private readonly float _a1;
        private float _x1;
        private float _y1;

        public HeadShadowFilter(int sampleRate, float lateralAmount)
        {
            // lateralAmount in [0, 1] -> 对侧耳高频衰减 0 ~ -10 dB（低频保持 0 dB）
            var alpha = Math.Clamp(1.0f - 0.72f * lateralAmount, 0.25f, 1.0f);
            var omega0 = 2f * MathF.PI * 1650f / sampleRate;
            var k = MathF.Tan(omega0 * 0.5f);
            var denom = 1f + k;
            _b0 = (alpha + k) / denom;
            _b1 = (k - alpha) / denom;
            _a1 = (k - 1f) / denom;
            _x1 = 0f;
            _y1 = 0f;
        }

        public float Process(float x)
        {
            var y = _b0 * x + _b1 * _x1 - _a1 * _y1;
            _x1 = x;
            _y1 = y;
            return y;
        }

        public void Reset()
        {
            _x1 = 0f;
            _y1 = 0f;
        }
    }

    // 轻量 Biquad 滤波器（Peaking / HighShelf），用于耳廓高度/后方定位与 LFE 柔化。
    private struct BiquadFilter
    {
        private float _b0, _b1, _b2, _a1, _a2;
        private float _z1, _z2;

        public static BiquadFilter CreatePeaking(int sampleRate, float freqHz, float gainDb, float q)
        {
            var clampedFreq = Math.Clamp(freqHz, 40f, sampleRate * 0.45f);
            var a = MathF.Pow(10f, gainDb / 40f);
            var w0 = 2f * MathF.PI * clampedFreq / sampleRate;
            var cosW0 = MathF.Cos(w0);
            var alpha = MathF.Sin(w0) / (2f * q);

            var b0 = 1f + alpha * a;
            var b1 = -2f * cosW0;
            var b2 = 1f - alpha * a;
            var a0 = 1f + alpha / a;
            var a1 = -2f * cosW0;
            var a2 = 1f - alpha / a;

            return FromNormalized(b0, b1, b2, a0, a1, a2);
        }

        public static BiquadFilter CreateHighShelf(int sampleRate, float freqHz, float gainDb, float slope)
        {
            var clampedFreq = Math.Clamp(freqHz, 40f, sampleRate * 0.45f);
            var a = MathF.Pow(10f, gainDb / 40f);
            var w0 = 2f * MathF.PI * clampedFreq / sampleRate;
            var cosW0 = MathF.Cos(w0);
            var sinW0 = MathF.Sin(w0);
            var alpha = sinW0 * 0.5f * MathF.Sqrt((a + 1f / a) * (1f / slope - 1f) + 2f);
            var twoSqrtAAlpha = 2f * MathF.Sqrt(a) * alpha;

            var b0 = a * ((a + 1f) + (a - 1f) * cosW0 + twoSqrtAAlpha);
            var b1 = -2f * a * ((a - 1f) + (a + 1f) * cosW0);
            var b2 = a * ((a + 1f) + (a - 1f) * cosW0 - twoSqrtAAlpha);
            var a0 = (a + 1f) - (a - 1f) * cosW0 + twoSqrtAAlpha;
            var a1 = 2f * ((a - 1f) - (a + 1f) * cosW0);
            var a2 = (a + 1f) - (a - 1f) * cosW0 - twoSqrtAAlpha;

            return FromNormalized(b0, b1, b2, a0, a1, a2);
        }

        private static BiquadFilter FromNormalized(float b0, float b1, float b2, float a0, float a1, float a2)
        {
            var invA0 = 1f / a0;
            return new BiquadFilter
            {
                _b0 = b0 * invA0,
                _b1 = b1 * invA0,
                _b2 = b2 * invA0,
                _a1 = a1 * invA0,
                _a2 = a2 * invA0
            };
        }

        public float Process(float x)
        {
            var y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return y;
        }

        public void Reset()
        {
            _z1 = 0f;
            _z2 = 0f;
        }
    }

    // 4 抽头早期反射外化网络（5~19ms，无浑浊长混响尾音），消除耳机“头中效应”。
    private sealed class EarlyReflectionRoom
    {
        private readonly float[] _bufL;
        private readonly float[] _bufR;
        private readonly int _mask;
        private readonly int _tap1L, _tap2L, _tap3L, _tap4L;
        private readonly int _tap1R, _tap2R, _tap3R, _tap4R;
        private readonly float _lpCoeff;
        private float _lpStateL;
        private float _lpStateR;
        private int _writeIndex;

        public EarlyReflectionRoom(int sampleRate)
        {
            var maxSamples = Math.Max(256, (int)(sampleRate * 0.025f));
            var size = 1;
            while (size < maxSamples) size <<= 1;
            _bufL = new float[size];
            _bufR = new float[size];
            _mask = size - 1;

            // 左右耳微幅不对称的质数毫秒早期反射，避免金属感梳状滤波
            _tap1L = Math.Clamp((int)(sampleRate * 0.0053f), 1, _mask);
            _tap2L = Math.Clamp((int)(sampleRate * 0.0091f), 1, _mask);
            _tap3L = Math.Clamp((int)(sampleRate * 0.0137f), 1, _mask);
            _tap4L = Math.Clamp((int)(sampleRate * 0.0181f), 1, _mask);

            _tap1R = Math.Clamp((int)(sampleRate * 0.0061f), 1, _mask);
            _tap2R = Math.Clamp((int)(sampleRate * 0.0103f), 1, _mask);
            _tap3R = Math.Clamp((int)(sampleRate * 0.0149f), 1, _mask);
            _tap4R = Math.Clamp((int)(sampleRate * 0.0193f), 1, _mask);

            // 约 4.5 kHz 空气吸收一阶低通
            var rc = 1f / (2f * MathF.PI * 4500f);
            var dt = 1f / sampleRate;
            _lpCoeff = dt / (rc + dt);
        }

        public void ProcessSample(float inL, float inR, out float outL, out float outR)
        {
            _bufL[_writeIndex] = inL;
            _bufR[_writeIndex] = inR;

            var rawL =
                _bufL[(_writeIndex - _tap1L) & _mask] * 0.42f +
                _bufR[(_writeIndex - _tap2L) & _mask] * 0.30f -
                _bufL[(_writeIndex - _tap3L) & _mask] * 0.22f +
                _bufR[(_writeIndex - _tap4L) & _mask] * 0.16f;

            var rawR =
                _bufR[(_writeIndex - _tap1R) & _mask] * 0.42f +
                _bufL[(_writeIndex - _tap2R) & _mask] * 0.30f -
                _bufR[(_writeIndex - _tap3R) & _mask] * 0.22f +
                _bufL[(_writeIndex - _tap4R) & _mask] * 0.16f;

            _lpStateL += _lpCoeff * (rawL - _lpStateL);
            _lpStateR += _lpCoeff * (rawR - _lpStateR);

            outL = _lpStateL;
            outR = _lpStateR;

            _writeIndex = (_writeIndex + 1) & _mask;
        }

        public void Reset()
        {
            Array.Clear(_bufL);
            Array.Clear(_bufR);
            _lpStateL = 0f;
            _lpStateR = 0f;
            _writeIndex = 0;
        }
    }
}
