namespace PjSip.Net.Messaging;

public sealed record SipMessage
{
    public required string From { get; init; }
    public required string To { get; init; }
    public required string Body { get; init; }
    public string ContentType { get; init; } = "text/plain";
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
