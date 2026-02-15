namespace PjSip.Net.Presence;

public sealed record BuddyInfo
{
    public required string Uri { get; init; }
    public string? DisplayName { get; init; }
    public required BuddyState State { get; init; }
    public string? StatusText { get; init; }
    public DateTime LastUpdated { get; init; }
}
