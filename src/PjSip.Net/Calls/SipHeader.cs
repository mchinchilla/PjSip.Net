namespace PjSip.Net.Calls;

public sealed record SipHeader
{
    public required string Name { get; init; }
    public required string Value { get; init; }
}
