using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PjSip.Net.Events;
using PjSip.Net.Exceptions;
using PjSip.Net.Interop.Generated;
using PjSip.Net.Logging;
using PjSip.Net.Transport;

namespace PjSip.Net.Internal;

/// <summary>
/// Manages the PJSIP Endpoint lifecycle. Wraps pj.Endpoint creation,
/// configuration, transport setup, and destruction.
/// </summary>
internal sealed class PjSipEndpointManager : IDisposable
{
    private readonly ILogger<PjSipEndpointManager> _logger;
    private readonly PjSipLoggerAdapter _loggerAdapter;
    private readonly SipPhoneOptions _options;
    private readonly PjSipThreadSafeInvoker _invoker;
    private PjSipManagedEndpoint? _endpoint;
    private volatile bool _initialized;
    private volatile bool _disposed;

    public PjSipEndpointManager(
        IOptions<SipPhoneOptions> options,
        ILogger<PjSipEndpointManager> logger,
        PjSipLoggerAdapter loggerAdapter)
    {
        _options = options.Value;
        _logger = logger;
        _loggerAdapter = loggerAdapter;
        _invoker = new PjSipThreadSafeInvoker();
    }

    internal PjSipThreadSafeInvoker Invoker => _invoker;
    internal PjSipManagedEndpoint? Endpoint => _endpoint;
    internal bool IsIOS => OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst();

    internal event EventHandler<TransportStateChangedEventArgs>? TransportStateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) return;

        if (!NativeAvailability.IsAvailable)
        {
            _logger.LogWarning("Native PJSIP library not available – running in stub mode");
            _initialized = true;
            return;
        }

        await _invoker.InvokeAsync(() =>
        {
            _logger.LogInformation("Initializing PJSIP endpoint (UserAgent={UserAgent})", _options.UserAgent);

            _endpoint = new PjSipManagedEndpoint(_logger);
            _endpoint.TransportStateChanged += (s, e) => TransportStateChanged?.Invoke(this, e);

            _endpoint.libCreate();
            // Note: the thread calling libCreate() is automatically registered
            // with pjlib.  No need to call libRegisterThread() here.

            // Configure EpConfig
            using var epConfig = new EpConfig();

            // UA config
            epConfig.uaConfig.userAgent = _options.UserAgent;
            epConfig.uaConfig.maxCalls = (uint)_options.MaxCalls;

            // STUN servers
            if (_options.Nat.EnableStun && _options.Nat.StunServers.Count > 0)
            {
                var stunServers = new StringVector();
                foreach (var server in _options.Nat.StunServers)
                    stunServers.Add(server);
                epConfig.uaConfig.stunServer = stunServers;
            }

            // Log config
            epConfig.logConfig.level = (uint)_options.LogLevel;
            epConfig.logConfig.consoleLevel = (uint)_options.LogLevel;
            epConfig.logConfig.writer = _loggerAdapter.GetOrCreateLogWriter();

            _endpoint.libInit(epConfig);

            // Create transports
            foreach (var transportOpt in _options.Transports)
            {
                CreateTransport(transportOpt);
            }

            // If no transports configured, create default UDP
            if (_options.Transports.Count == 0)
            {
                CreateTransport(new SipTransportOptions { Type = SipTransportType.Udp, Port = 5060 });
            }

            _endpoint.libStart();

            // On iOS, PJSIP tries to open the CoreAudio device during makeCall
            // which fails with error -2002889396 if the AudioUnit can't start.
            // Use null sound device at init so makeCall succeeds (SIP signaling works),
            // then switch to real device when media becomes active.
            if (OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst())
            {
                try
                {
                    var devCount = _endpoint.audDevManager().enumDev2().Count;
                    _logger.LogInformation("iOS: {DeviceCount} audio device(s) found — using null sound device at init", devCount);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "iOS: audio device enumeration failed");
                }
                try { _endpoint.audDevManager().setNullDev(); }
                catch (Exception ex) { _logger.LogError(ex, "iOS: setNullDev failed"); }
            }

            _initialized = true;
            _logger.LogInformation("PJSIP endpoint initialized successfully");
        });
    }

    public async Task ShutdownAsync()
    {
        if (!_initialized || _disposed) return;

        if (_endpoint is null)
        {
            _initialized = false;
            return;
        }

        await _invoker.InvokeAsync(() =>
        {
            _logger.LogInformation("Shutting down PJSIP endpoint");

            try
            {
                _endpoint.hangupAllCalls();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error hanging up calls during shutdown");
            }

            try
            {
                _endpoint.libDestroy();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error destroying endpoint");
            }

            _endpoint = null;
            _initialized = false;
            _logger.LogInformation("PJSIP endpoint shut down");
        });
    }

    /// <summary>
    /// On iOS, re-establishes the null sound device after a call disconnects.
    /// This ensures the next makeCall succeeds (null device provides the
    /// conference bridge clock without opening CoreAudio).
    /// Must be called on the PJSIP worker thread.
    /// </summary>
    internal void RestoreNullDeviceIfIOS()
    {
        if (!IsIOS || _endpoint is null) return;

        try
        {
            _endpoint.audDevManager().setNullDev();
            _logger.LogDebug("iOS: null sound device restored after call disconnect");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "iOS: failed to restore null sound device");
        }
    }

    /// <summary>
    /// On iOS, attempts to switch from null device to real CoreAudio device.
    /// Returns true if successful, false if CoreAudio couldn't open (in which
    /// case the null device remains active — media will still connect but with
    /// no real audio).
    /// Must be called on the PJSIP worker thread.
    /// </summary>
    internal bool TrySwitchToRealAudioDevice()
    {
        if (!IsIOS || _endpoint is null) return false;

        try
        {
            // Invoke the platform callback so the app can (re-)activate
            // AVAudioSession before PJSIP opens the AudioUnit.
            try
            {
                _options.OnAudioDeviceActivation?.Invoke();
            }
            catch (Exception cbEx)
            {
                _logger.LogWarning(cbEx, "iOS: OnAudioDeviceActivation callback failed");
            }

            var audMgr = _endpoint.audDevManager();

            // Refresh device list so PJSIP picks up any route changes
            try { audMgr.refreshDevs(); }
            catch (Exception rdEx) { _logger.LogDebug(rdEx, "iOS: refreshDevs failed (non-fatal)"); }

            audMgr.setCaptureDev(0);
            audMgr.setPlaybackDev(0);
            _logger.LogInformation("iOS: switched to real CoreAudio device successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "iOS: failed to switch to real CoreAudio device — continuing with null device (no audio)");
            // Re-establish null device since the failed attempt may have left
            // the audio subsystem in a bad state
            try { _endpoint.audDevManager().setNullDev(); }
            catch (Exception ex2) { _logger.LogError(ex2, "iOS: failed to re-establish null device after real device switch failure"); }
            return false;
        }
    }

    private void CreateTransport(SipTransportOptions transportOpt)
    {
        using var cfg = new TransportConfig();
        cfg.port = (uint)transportOpt.Port;
        if (transportOpt.BoundAddress is not null)
            cfg.boundAddress = transportOpt.BoundAddress;

        var pjType = MapTransportType(transportOpt.Type);

        try
        {
            _endpoint!.transportCreate(pjType, cfg);
            _logger.LogInformation("Created {TransportType} transport on port {Port}",
                transportOpt.Type, transportOpt.Port);
        }
        catch (Exception ex)
        {
            throw new SipTransportException(
                $"Failed to create {transportOpt.Type} transport on port {transportOpt.Port}", ex);
        }
    }

    internal static pjsip_transport_type_e MapTransportType(SipTransportType type)
    {
        return type switch
        {
            SipTransportType.Udp => pjsip_transport_type_e.PJSIP_TRANSPORT_UDP,
            SipTransportType.Tcp => pjsip_transport_type_e.PJSIP_TRANSPORT_TCP,
            SipTransportType.Tls => pjsip_transport_type_e.PJSIP_TRANSPORT_TLS,
            SipTransportType.Udp6 => pjsip_transport_type_e.PJSIP_TRANSPORT_UDP6,
            SipTransportType.Tcp6 => pjsip_transport_type_e.PJSIP_TRANSPORT_TCP6,
            SipTransportType.Tls6 => pjsip_transport_type_e.PJSIP_TRANSPORT_TLS6,
            _ => pjsip_transport_type_e.PJSIP_TRANSPORT_UDP
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _invoker.Dispose();
    }
}
