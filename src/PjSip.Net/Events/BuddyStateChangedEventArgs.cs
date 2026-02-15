using PjSip.Net.Presence;

namespace PjSip.Net.Events;

public sealed class BuddyStateChangedEventArgs : SipEventArgs
{
    public required ISipBuddy Buddy { get; init; }
    public required BuddyState OldState { get; init; }
    public required BuddyState NewState { get; init; }
    public string? StatusText { get; init; }
}
