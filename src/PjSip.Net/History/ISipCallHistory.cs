namespace PjSip.Net.History;

public interface ISipCallHistory
{
    IReadOnlyList<CallHistoryEntry> Entries { get; }
    IReadOnlyList<CallHistoryEntry> GetMissedCalls();
    IReadOnlyList<CallHistoryEntry> GetIncomingCalls();
    IReadOnlyList<CallHistoryEntry> GetOutgoingCalls();
    void Clear();
    event EventHandler<CallHistoryEntry>? EntryAdded;
}
