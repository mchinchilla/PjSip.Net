using PjSip.Net.Events;

namespace PjSip.Net.Presence;

public interface ISipPresenceManager
{
    IReadOnlyList<ISipBuddy> Buddies { get; }
    ISipBuddy AddBuddy(string uri);
    void RemoveBuddy(ISipBuddy buddy);
    Task SetMyPresenceAsync(BuddyState state, string? statusText = null, CancellationToken ct = default);
    BuddyState MyState { get; }
    event EventHandler<BuddyStateChangedEventArgs>? BuddyStateChanged;
}
