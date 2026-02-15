using Microsoft.Extensions.Logging;
using PjSip.Net.Events;
using PjSip.Net.Interop.Generated;
using PjSip.Net.Transport;

namespace PjSip.Net.Internal;

/// <summary>
/// SWIG Director subclass of <see cref="Endpoint"/> that receives native
/// callbacks (transport state, IP change, NAT detection, media events)
/// and exposes them as .NET events for the rest of the managed layer.
/// </summary>
internal sealed class PjSipManagedEndpoint : Endpoint
{
    private readonly ILogger _logger;

    public PjSipManagedEndpoint(ILogger logger)
    {
        _logger = logger;
    }

    internal event EventHandler<TransportStateChangedEventArgs>? TransportStateChanged;
    internal event EventHandler<EventArgs>? IpChangeProgress;
    internal event EventHandler<EventArgs>? NatDetectionComplete;

    public override void onTransportState(OnTransportStateParam prm)
    {
        try
        {
            _logger.LogDebug("Transport state changed: type={Type} state={State} lastError={Error}",
                prm.type, prm.state, prm.lastError);

            TransportStateChanged?.Invoke(this, new TransportStateChangedEventArgs
            {
                TransportType = SipTransportType.Udp, // best-effort mapping
                State = prm.state.ToString(),
                LastError = prm.lastError != 0 ? $"Error {prm.lastError}" : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling onTransportState callback");
        }
    }

    public override void onIpChangeProgress(OnIpChangeProgressParam prm)
    {
        try
        {
            _logger.LogInformation("IP change progress callback received");
            IpChangeProgress?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling onIpChangeProgress callback");
        }
    }

    public override void onNatDetectionComplete(OnNatDetectionCompleteParam prm)
    {
        try
        {
            _logger.LogInformation("NAT detection complete: status={Status}", prm.status);
            NatDetectionComplete?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling onNatDetectionComplete callback");
        }
    }

    public override void onMediaEvent(OnMediaEventParam prm)
    {
        try
        {
            _logger.LogDebug("Media event received");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling onMediaEvent callback");
        }
    }
}
