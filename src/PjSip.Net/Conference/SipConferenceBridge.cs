using Microsoft.Extensions.Logging;
using PjSip.Net.Calls;
using PjSip.Net.Internal;
using PjSip.Net.Interop.Generated;

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
        ISipCall[] existingParticipants;
        lock (_lock)
        {
            existingParticipants = _participants.ToArray();
            _participants.Add(call);
        }

        _logger.LogInformation("Added call {CallId} to conference bridge", call.Id);

        if (call is ManagedCall managed && managed.AudioMedia is not null)
        {
            _endpointManager.Invoker.Invoke(() =>
            {
                // Connect this call's audio to all existing participants bidirectionally
                foreach (var existing in existingParticipants)
                {
                    if (existing is ManagedCall existingManaged && existingManaged.AudioMedia is not null)
                    {
                        try
                        {
                            managed.AudioMedia.startTransmit(existingManaged.AudioMedia);
                            existingManaged.AudioMedia.startTransmit(managed.AudioMedia);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error connecting conference audio between {Call1} and {Call2}",
                                call.Id, existing.Id);
                        }
                    }
                }
            });
        }
    }

    public void RemoveParticipant(ISipCall call)
    {
        ISipCall[] remainingParticipants;
        lock (_lock)
        {
            _participants.Remove(call);
            remainingParticipants = _participants.ToArray();
        }

        _logger.LogInformation("Removed call {CallId} from conference bridge", call.Id);

        if (call is ManagedCall managed && managed.AudioMedia is not null)
        {
            _endpointManager.Invoker.Invoke(() =>
            {
                foreach (var remaining in remainingParticipants)
                {
                    if (remaining is ManagedCall remainingManaged && remainingManaged.AudioMedia is not null)
                    {
                        try
                        {
                            managed.AudioMedia.stopTransmit(remainingManaged.AudioMedia);
                            remainingManaged.AudioMedia.stopTransmit(managed.AudioMedia);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error disconnecting conference audio for {CallId}", call.Id);
                        }
                    }
                }
            });
        }
    }

    public void MergeAll(IEnumerable<ISipCall> calls)
    {
        lock (_lock)
        {
            _participants.Clear();
            _participants.AddRange(calls);
        }

        _logger.LogInformation("Merged {Count} calls into conference", _participants.Count);

        _endpointManager.Invoker.Invoke(() =>
        {
            var managedCalls = _participants.OfType<ManagedCall>()
                .Where(c => c.AudioMedia is not null).ToArray();

            // Connect all pairs bidirectionally
            for (int i = 0; i < managedCalls.Length; i++)
            {
                for (int j = i + 1; j < managedCalls.Length; j++)
                {
                    try
                    {
                        managedCalls[i].AudioMedia!.startTransmit(managedCalls[j].AudioMedia!);
                        managedCalls[j].AudioMedia!.startTransmit(managedCalls[i].AudioMedia!);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error connecting conference pair");
                    }
                }
            }
        });
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

        _endpointManager.Invoker.Invoke(() =>
        {
            var managedCalls = toSplit.OfType<ManagedCall>()
                .Where(c => c.AudioMedia is not null).ToArray();

            // Disconnect all pairs
            for (int i = 0; i < managedCalls.Length; i++)
            {
                for (int j = i + 1; j < managedCalls.Length; j++)
                {
                    try
                    {
                        managedCalls[i].AudioMedia!.stopTransmit(managedCalls[j].AudioMedia!);
                        managedCalls[j].AudioMedia!.stopTransmit(managedCalls[i].AudioMedia!);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error disconnecting conference pair");
                    }
                }
            }

            // Reconnect each call to default sound devices (playback + capture)
            var ep = _endpointManager.Endpoint;
            if (ep is not null)
            {
                foreach (var mc in managedCalls)
                {
                    try
                    {
                        var audMgr = ep.audDevManager();
                        var playMedia = audMgr.getPlaybackDevMedia();
                        var capMedia = audMgr.getCaptureDevMedia();
                        mc.AudioMedia!.startTransmit(playMedia);
                        capMedia.startTransmit(mc.AudioMedia!);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error reconnecting call to sound device");
                    }
                }
            }
        });
    }
}
