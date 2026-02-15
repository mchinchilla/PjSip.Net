namespace PjSip.Net.Tones;

public sealed record ToneDescriptor
{
    public required int Frequency1 { get; init; }
    public int Frequency2 { get; init; }
    public required int OnMs { get; init; }
    public required int OffMs { get; init; }
    public int Volume { get; init; } = 16000;
}
