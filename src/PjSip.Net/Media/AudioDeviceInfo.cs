namespace PjSip.Net.Media;

public sealed class AudioDeviceInfo
{
    public required int DeviceId { get; init; }
    public required string Name { get; init; }
    public int InputChannels { get; init; }
    public int OutputChannels { get; init; }
    public string? Driver { get; init; }
}
