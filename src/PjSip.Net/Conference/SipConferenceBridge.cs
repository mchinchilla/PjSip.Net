using Microsoft.Extensions.Logging;
using PjSip.Net.Calls;
using PjSip.Net.Internal;

namespace PjSip.Net.Conference;

internal sealed class SipConferenceBridge : ISipConferenceBridge
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;
    private readonly List<ISipCall> _participants = [];
    private readonly object _lock = new();

    public SipConferenceBridge(
        PjSipEndpointManager endpointManager,
        ILogger logger)
    {
        _endpointManager = endpointManager;
        _logger = logger;
    }

    public IReadOnlyList<ISipCall> Participants
    {
        get { lock (_lock) return _participants.ToArray(); }
    }

    public void AddParticipant(ISipCall call)
    {
        lock (_lock) _participants.Add(call);
        _logger.LogInformation("Added call {CallId} to conference bridge", call.Id);

        // TODO: When SWIG-generated classes are available:
        // 1. Get AudioMedia from the call
        // 2. Connect all existing participants' AudioMedia to this one
        // 3. Connect this one's AudioMedia to all existing participants
    }

    public void RemoveParticipant(ISipCall call)
    {
        lock (_lock) _participants.Remove(call);
        _logger.LogInformation("Removed call {CallId} from conference bridge", call.Id);

        // TODO: When SWIG-generated classes are available:
        // 1. Disconnect this call's AudioMedia from all others
    }

    public void MergeAll(IEnumerable<ISipCall> calls)
    {
        lock (_lock)
        {
            _participants.Clear();
            _participants.AddRange(calls);
        }

        _logger.LogInformation("Merged {Count} calls into conference", _participants.Count);

        // TODO: When SWIG-generated classes are available:
        // 1. For each pair of calls, connect their AudioMedia ports
    }

    public void SplitAll()
    {
        List<ISipCall> toSplit;
        lock (_lock)
        {
            toSplit = [.. _participants];
            _participants.Clear();
        }

        _logger.LogInformation("Split {Count} calls from conference", toSplit.Count);

        // TODO: When SWIG-generated classes are available:
        // 1. Disconnect all AudioMedia cross-connections
        // 2. Reconnect each call to default sound device only
    }
}
