using ManagedBass;

namespace SimpleAudio;

public sealed record AudioOutputDevice(int DeviceId, string Name, string? Driver, bool IsSystemDefault)
{
    public static AudioOutputDevice SystemDefault { get; } =
        new(Bass.DefaultDevice, "系统默认设备", null, true);
}
