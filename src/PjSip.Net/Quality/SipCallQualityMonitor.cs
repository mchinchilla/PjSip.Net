using Microsoft.Extensions.Logging;
using PjSip.Net.Calls;
using PjSip.Net.Internal;

namespace PjSip.Net.Quality;

internal sealed class SipCallQualityMonitor : ISipCallQualityMonitor
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;

    public SipCallQualityMonitor(
        PjSipEndpointManager endpointManager,
        ILogger logger)
    {
        _endpointManager = endpointManager;
        _logger = logger;
    }

    public event EventHandler<CallQualityEventArgs>? QualityReportAvailable;

    public CallQualityInfo? GetQuality(ISipCall call)
    {
        // TODO: When SWIG-generated classes are available:
        // 1. Get pj.StreamStat from the call's media stream
        // 2. Map RTP stats to CallQualityInfo
        _logger.LogDebug("Getting call quality for {CallId}", call.Id);
        return null;
    }

    public async Task<CallQualityInfo?> GetQualityAsync(ISipCall call, CancellationToken ct = default)
    {
        return await _endpointManager.Invoker.InvokeAsync(() =>
        {
            // TODO: When SWIG-generated classes are available:
            // 1. Get pj.StreamStat from the call's media stream
            // 2. Map RTP stats to CallQualityInfo
            _logger.LogDebug("Getting call quality async for {CallId}", call.Id);
            return (CallQualityInfo?)null;
        });
    }

    internal void OnQualityReport(ISipCall call, CallQualityInfo quality)
    {
        QualityReportAvailable?.Invoke(this, new CallQualityEventArgs
        {
            Call = call,
            Quality = quality
        });
    }
}
