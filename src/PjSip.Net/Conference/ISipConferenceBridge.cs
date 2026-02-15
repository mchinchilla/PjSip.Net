using PjSip.Net.Calls;

namespace PjSip.Net.Conference;

public interface ISipConferenceBridge
{
    IReadOnlyList<ISipCall> Participants { get; }
    void AddParticipant(ISipCall call);
    void RemoveParticipant(ISipCall call);
    void MergeAll(IEnumerable<ISipCall> calls);
    void SplitAll();
}
