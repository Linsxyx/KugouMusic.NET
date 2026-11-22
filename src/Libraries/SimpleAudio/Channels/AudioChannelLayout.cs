namespace SimpleAudio;

// 多声道输出适配策略：自动跟随设备扬声器数、强制立体声或原样直出。
public enum AudioChannelMode
{
    Auto = 0,
    ForceStereo = 1,
    Passthrough = 2
}

internal enum AudioChannelRole
{
    FrontLeft,
    FrontRight,
    FrontCenter,
    Lfe,
    BackLeft,
    BackRight,
    SideLeft,
    SideRight,
    TopFrontLeft,
    TopFrontRight,
    TopBackLeft,
    TopBackRight,
    Unknown
}

// 描述音频流或目标输出设备的声道拓扑结构，用于跨格式统一声道自适配。
internal sealed class AudioChannelLayout
{
    public const uint MaskMono = 0x0000_0004;      // FC
    public const uint MaskStereo = 0x0000_0003;    // FL | FR
    public const uint MaskQuad = 0x0000_0033;      // FL | FR | BL | BR
    public const uint Mask51 = 0x0000_003F;        // FL | FR | FC | LFE | BL | BR
    public const uint Mask71 = 0x0000_063F;        // FL | FR | FC | LFE | BL | BR | SL | SR

    public static AudioChannelLayout Mono { get; } = new(
        "1.0",
        MaskMono,
        [AudioChannelRole.FrontCenter]);

    public static AudioChannelLayout Stereo { get; } = new(
        "2.0",
        MaskStereo,
        [AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight]);

    public static AudioChannelLayout Quad { get; } = new(
        "4.0",
        MaskQuad,
        [
            AudioChannelRole.FrontLeft,
            AudioChannelRole.FrontRight,
            AudioChannelRole.BackLeft,
            AudioChannelRole.BackRight
        ]);

    public static AudioChannelLayout Surround51 { get; } = new(
        "5.1",
        Mask51,
        [
            AudioChannelRole.FrontLeft,
            AudioChannelRole.FrontRight,
            AudioChannelRole.FrontCenter,
            AudioChannelRole.Lfe,
            AudioChannelRole.BackLeft,
            AudioChannelRole.BackRight
        ]);

    public static AudioChannelLayout Surround71 { get; } = new(
        "7.1",
        Mask71,
        [
            AudioChannelRole.FrontLeft,
            AudioChannelRole.FrontRight,
            AudioChannelRole.FrontCenter,
            AudioChannelRole.Lfe,
            AudioChannelRole.BackLeft,
            AudioChannelRole.BackRight,
            AudioChannelRole.SideLeft,
            AudioChannelRole.SideRight
        ]);

    public string Name { get; }
    public uint WaveChannelMask { get; }
    public AudioChannelRole[] Roles { get; }
    public int Channels => Roles.Length;

    public AudioChannelLayout(string name, uint waveChannelMask, AudioChannelRole[] roles)
    {
        Name = name;
        WaveChannelMask = waveChannelMask;
        Roles = roles;
    }

    // 根据标准 SMPTE / WAVE 声道数推导布局（适用于 FLAC/AAC/WAV/DSD/OGG 等常见格式）。
    public static AudioChannelLayout FromStandardChannelCount(int channels)
    {
        return channels switch
        {
            <= 1 => Mono,
            2 => Stereo,
            3 => new AudioChannelLayout("2.1", 0x0000_0007,
                [AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.FrontCenter]),
            4 => Quad,
            5 => new AudioChannelLayout("5.0", 0x0000_0037,
                [
                    AudioChannelRole.FrontLeft, AudioChannelRole.FrontRight, AudioChannelRole.FrontCenter,
                    AudioChannelRole.BackLeft, AudioChannelRole.BackRight
                ]),
            6 => Surround51,
            8 => Surround71,
            _ => CreateGenericLayout(channels)
        };
    }

    // 根据 AV3A 帧头的 ChannelConfig 与声道数推导精确声道角色顺序。
    public static AudioChannelLayout FromAv3aConfig(int channels, uint channelConfig)
    {
        return (channelConfig, channels) switch
        {
            (0, 1) => Mono,
            (1, 2) => Stereo,
            (6, 4) => new AudioChannelLayout("4.0", MaskQuad,
                [
                    AudioChannelRole.FrontLeft,
                    AudioChannelRole.FrontRight,
                    AudioChannelRole.SideLeft,
                    AudioChannelRole.SideRight
                ]),
            (2, 6) => new AudioChannelLayout("5.1", Mask51,
                [
                    AudioChannelRole.FrontLeft,
                    AudioChannelRole.FrontRight,
                    AudioChannelRole.FrontCenter,
                    AudioChannelRole.Lfe,
                    AudioChannelRole.SideLeft,
                    AudioChannelRole.SideRight
                ]),
            (3, 8) => Surround71,
            (7, 8) => new AudioChannelLayout("5.1.2", 0x0000_503F,
                [
                    AudioChannelRole.FrontLeft,
                    AudioChannelRole.FrontRight,
                    AudioChannelRole.FrontCenter,
                    AudioChannelRole.Lfe,
                    AudioChannelRole.SideLeft,
                    AudioChannelRole.SideRight,
                    AudioChannelRole.TopFrontLeft,
                    AudioChannelRole.TopFrontRight
                ]),
            (8, 10) => new AudioChannelLayout("5.1.4", 0x0002_D03F,
                [
                    AudioChannelRole.FrontLeft,
                    AudioChannelRole.FrontRight,
                    AudioChannelRole.FrontCenter,
                    AudioChannelRole.Lfe,
                    AudioChannelRole.SideLeft,
                    AudioChannelRole.SideRight,
                    AudioChannelRole.TopFrontLeft,
                    AudioChannelRole.TopFrontRight,
                    AudioChannelRole.TopBackLeft,
                    AudioChannelRole.TopBackRight
                ]),
            (9, 10) => new AudioChannelLayout("7.1.2", 0x0000_563F,
                [
                    AudioChannelRole.FrontLeft,
                    AudioChannelRole.FrontRight,
                    AudioChannelRole.FrontCenter,
                    AudioChannelRole.Lfe,
                    AudioChannelRole.BackLeft,
                    AudioChannelRole.BackRight,
                    AudioChannelRole.SideLeft,
                    AudioChannelRole.SideRight,
                    AudioChannelRole.TopFrontLeft,
                    AudioChannelRole.TopFrontRight
                ]),
            (10, 12) => new AudioChannelLayout("7.1.4", 0x0002_D63F,
                [
                    AudioChannelRole.FrontLeft,
                    AudioChannelRole.FrontRight,
                    AudioChannelRole.FrontCenter,
                    AudioChannelRole.Lfe,
                    AudioChannelRole.BackLeft,
                    AudioChannelRole.BackRight,
                    AudioChannelRole.SideLeft,
                    AudioChannelRole.SideRight,
                    AudioChannelRole.TopFrontLeft,
                    AudioChannelRole.TopFrontRight,
                    AudioChannelRole.TopBackLeft,
                    AudioChannelRole.TopBackRight
                ]),
            _ => FromStandardChannelCount(channels)
        };
    }

    // 结合源布局、输出设备扬声器数、用户策略与管线上限，决定最终输出布局。
    public static AudioChannelLayout ResolveTargetLayout(
        AudioChannelLayout source,
        int deviceSpeakers,
        AudioChannelMode mode,
        int maxSupportedChannels = 8)
    {
        var safeMax = Math.Max(2, maxSupportedChannels);
        var effectiveDeviceSpeakers = deviceSpeakers <= 0 ? 2 : deviceSpeakers;

        if (mode == AudioChannelMode.ForceStereo)
        {
            return source.Channels <= 2 ? source : Stereo;
        }

        if (mode == AudioChannelMode.Passthrough)
        {
            if (source.Channels <= safeMax)
            {
                return source;
            }

            return SelectLargestStandardLayout(safeMax);
        }

        // Auto 模式：优先匹配设备真实扬声器能力，同时不超过下游 FX/管线安全上限。
        var budget = Math.Min(effectiveDeviceSpeakers, safeMax);

        if (source.Channels <= 2)
        {
            return source;
        }

        // 若源本身就是标准平面布局且未超出设备能力，直接原样直出。
        if (source.Channels <= budget && !HasHeightChannels(source))
        {
            return source;
        }

        // 对于带天空声道的空间音频（如 5.1.2 / 7.1.4）或超设备声道数的多声道音频，按设备拓扑匹配最接近的标准平面布局。
        return SelectLargestStandardLayout(Math.Min(source.Channels, budget));
    }

    public bool HasSameRoleOrder(AudioChannelLayout other)
    {
        if (Channels != other.Channels)
        {
            return false;
        }

        for (var i = 0; i < Roles.Length; i++)
        {
            if (Roles[i] != other.Roles[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasHeightChannels(AudioChannelLayout layout)
    {
        foreach (var role in layout.Roles)
        {
            if (role is AudioChannelRole.TopFrontLeft or AudioChannelRole.TopFrontRight
                or AudioChannelRole.TopBackLeft or AudioChannelRole.TopBackRight)
            {
                return true;
            }
        }

        return false;
    }

    private static AudioChannelLayout SelectLargestStandardLayout(int maxChannels)
    {
        if (maxChannels >= 8)
        {
            return Surround71;
        }

        if (maxChannels >= 6)
        {
            return Surround51;
        }

        if (maxChannels >= 4)
        {
            return Quad;
        }

        return Stereo;
    }

    private static AudioChannelLayout CreateGenericLayout(int channels)
    {
        var count = Math.Max(1, channels);
        var roles = new AudioChannelRole[count];
        for (var i = 0; i < count; i++)
        {
            roles[i] = i switch
            {
                0 => AudioChannelRole.FrontLeft,
                1 => AudioChannelRole.FrontRight,
                2 => AudioChannelRole.FrontCenter,
                3 => AudioChannelRole.Lfe,
                _ => i % 2 == 0 ? AudioChannelRole.SideLeft : AudioChannelRole.SideRight
            };
        }

        return new AudioChannelLayout($"{count}ch", 0, roles);
    }
}
