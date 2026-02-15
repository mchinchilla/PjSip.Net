using PjSip.Net.Calls;

namespace PjSip.Net.History;

internal sealed class SipCallHistory : ISipCallHistory
{
    private readonly LinkedList<CallHistoryEntry> _entries = new();
    private readonly object _lock = new();
    private readonly int _maxEntries;

    public SipCallHistory(int maxEntries = 1000)
    {
        _maxEntries = maxEntries;
    }

    public IReadOnlyList<CallHistoryEntry> Entries
    {
        get { lock (_lock) return [.. _entries]; }
    }

    public event EventHandler<CallHistoryEntry>? EntryAdded;

    public IReadOnlyList<CallHistoryEntry> GetMissedCalls()
    {
        lock (_lock) return _entries
            .Where(e => e.Direction == CallDirection.Incoming && e.FinalState != SipCallState.Confirmed)
            .ToArray();
    }

    public IReadOnlyList<CallHistoryEntry> GetIncomingCalls()
    {
        lock (_lock) return _entries
            .Where(e => e.Direction == CallDirection.Incoming)
            .ToArray();
    }

    public IReadOnlyList<CallHistoryEntry> GetOutgoingCalls()
    {
        lock (_lock) return _entries
            .Where(e => e.Direction == CallDirection.Outgoing)
            .ToArray();
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
    }

    internal void AddEntry(CallHistoryEntry entry)
    {
        lock (_lock)
        {
            _entries.AddLast(entry);
            while (_entries.Count > _maxEntries)
            {
                _entries.RemoveFirst(); // O(1) removal from head
            }
        }
        EntryAdded?.Invoke(this, entry);
    }
}
