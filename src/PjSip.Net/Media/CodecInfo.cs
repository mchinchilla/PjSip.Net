namespace PjSip.Net.Media;

public sealed record CodecInfo
{
    public required string CodecId { get; init; }
    public required string Description { get; init; }
    public int Priority { get; init; }
    public int ClockRate { get; init; }
    public int ChannelCount { get; init; }
}
