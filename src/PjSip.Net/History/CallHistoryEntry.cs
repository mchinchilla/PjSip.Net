using PjSip.Net.Calls;

namespace PjSip.Net.History;

public sealed record CallHistoryEntry
{
    public required string CallId { get; init; }
    public required string RemoteUri { get; init; }
    public string? RemoteDisplayName { get; init; }
    public required CallDirection Direction { get; init; }
    public required DateTime StartTime { get; init; }
    public DateTime? EndTime { get; init; }
    public TimeSpan Duration { get; init; }
    public required SipCallState FinalState { get; init; }
    public int StatusCode { get; init; }
    public string? AccountUri { get; init; }
}
