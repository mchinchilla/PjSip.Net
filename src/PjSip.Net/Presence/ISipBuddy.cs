using PjSip.Net.Events;

namespace PjSip.Net.Presence;

public interface ISipBuddy : IDisposable
{
    string Uri { get; }
    BuddyState State { get; }
    BuddyInfo Info { get; }

    event EventHandler<BuddyStateChangedEventArgs>? StateChanged;

    Task SubscribeAsync(CancellationToken ct = default);
    Task UnsubscribeAsync(CancellationToken ct = default);
}
