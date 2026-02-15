namespace PjSip.Net.Calls;

public sealed record SipCallInfo
{
    public required string CallId { get; init; }
    public required string RemoteUri { get; init; }
    public required string LocalUri { get; init; }
    public required SipCallState State { get; init; }
    public required CallDirection Direction { get; init; }
    public TimeSpan Duration { get; init; }
    public string? RemoteDisplayName { get; init; }
    public int StatusCode { get; init; }
    public string? StatusText { get; init; }
}
