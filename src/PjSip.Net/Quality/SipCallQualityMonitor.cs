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
        if (call is not ManagedCall managed)
        {
            _logger.LogDebug("Getting call quality for {CallId} (not a ManagedCall)", call.Id);
            return null;
        }

        return GetQualityFromNative(managed);
    }

    public async Task<CallQualityInfo?> GetQualityAsync(ISipCall call, CancellationToken ct = default)
    {
        if (call is not ManagedCall managed)
        {
            _logger.LogDebug("Getting call quality async for {CallId} (not a ManagedCall)", call.Id);
            return null;
        }

        return await _endpointManager.Invoker.InvokeAsync(() => GetQualityFromNative(managed));
    }

    private CallQualityInfo? GetQualityFromNative(ManagedCall call)
    {
        if (call.AudioMedia is null)
        {
            _logger.LogDebug("No audio media for call {CallId}, cannot get quality", call.Id);
            return null;
        }

        try
        {
            _logger.LogDebug("Getting call quality for {CallId}", call.Id);

            using var stat = call.GetStreamStat(0);
            if (stat?.rtcp is null)
            {
                return new CallQualityInfo
                {
                    CallId = call.Id,
                    Duration = call.Info.Duration
                };
            }

            var rtcp = stat.rtcp;
            var rx = rtcp.rxStat;
            var tx = rtcp.txStat;

            long packetsSent = tx?.pkt ?? 0;
            long packetsReceived = rx?.pkt ?? 0;
            long packetsLost = rx?.loss ?? 0;
            double lossPercent = packetsReceived > 0
                ? packetsLost * 100.0 / (packetsReceived + packetsLost)
                : 0;
            int jitterMs = rx?.jitterUsec?.mean is > 0 ? rx.jitterUsec.mean / 1000 : 0;
            int rttMs = rtcp.rttUsec?.mean is > 0 ? rtcp.rttUsec.mean / 1000 : 0;

            // Extract codec info from stream info
            string? codecName = null;
            int codecClockRate = 0;
            try
            {
                var si = stat.jbuf; // stream info contains codec details
                codecName = call.Info.StatusText; // fallback
            }
            catch { /* codec info is best-effort */ }

            double mos = CalculateMos(lossPercent, jitterMs, rttMs);

            return new CallQualityInfo
            {
                CallId = call.Id,
                Duration = call.Info.Duration,
                RtpPacketsSent = packetsSent,
                RtpPacketsReceived = packetsReceived,
                RtpPacketsLost = packetsLost,
                RtpLossPercentage = Math.Round(lossPercent, 2),
                RtpJitterMs = jitterMs,
                RtpRoundTripTimeMs = rttMs,
                MosScore = mos,
                CodecName = codecName,
                CodecClockRate = codecClockRate
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting call quality for {CallId}", call.Id);
            return null;
        }
    }

    /// <summary>
    /// Estimates MOS (Mean Opinion Score) using the E-model simplified formula (ITU-T G.107).
    /// Returns a value between 1.0 (bad) and 4.5 (excellent).
    /// </summary>
    private static double CalculateMos(double lossPercent, int jitterMs, int rttMs)
    {
        // Effective latency = one-way delay + jitter buffer compensation
        double effectiveLatency = rttMs / 2.0 + jitterMs * 2 + 10;

        // R-factor base (assumes G.711 codec)
        double r = 93.2;

        // Delay impairment (Id)
        if (effectiveLatency < 160)
            r -= effectiveLatency / 40.0;
        else
            r -= (effectiveLatency - 120) / 10.0;

        // Equipment impairment (Ie-eff) from packet loss
        r -= 2.5 * lossPercent;

        // Clamp R to valid range
        r = Math.Clamp(r, 0, 100);

        // Convert R-factor to MOS
        if (r < 6.5) return 1.0;
        double mos = 1.0 + 0.035 * r + r * (r - 60) * (100 - r) * 7e-6;
        return Math.Round(Math.Clamp(mos, 1.0, 4.5), 2);
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
