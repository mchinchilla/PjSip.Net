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

    /// <summary>
    /// pjsua transport ids returned by <c>transportCreate</c>, keyed by transport type. Accounts
    /// need these to pin themselves to one transport — see <see cref="TryGetTransportId"/>.
    /// Written only during <c>InitializeAsync</c> (before any account exists) and read afterwards.
    /// </summary>
    private readonly Dictionary<SipTransportType, int> _transportIds = [];

    public PjSipEndpointManager(
        IOptions<SipPhoneOptions> options,
        ILogger<PjSipEndpointManager> logger,
        PjSipLoggerAdapter loggerAdapter)
    {
        _options = options.Value;
        _logger = logger;
        _loggerAdapter = loggerAdapter;
        _invoker = new PjSipThreadSafeInvoker
        {
            // Keep the worker registered with pjlib so any native destructor
            // marshalled onto it (Call/Account/Buddy disposal) never aborts via
            // pj_thread_this(). No-op until the endpoint exists (post-libCreate).
            BeforeAction = () => EnsureThreadRegistered("PjSip-Worker")
        };
    }

    internal PjSipThreadSafeInvoker Invoker => _invoker;
    internal PjSipManagedEndpoint? Endpoint => _endpoint;
    internal SipPhoneOptions Options => _options;
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

            // Set null sound device BEFORE libStart to prevent the OS from
            // ducking system volume (macOS/Windows) and to avoid iOS AudioUnit
            // errors. The real audio device is opened when a call is established.
            if (OperatingSystem.IsIOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsWindows())
            {
                try
                {
                    var devCount = _endpoint.audDevManager().enumDev2().Count;
                    _logger.LogInformation("{DeviceCount} audio device(s) found — using null sound device at init", devCount);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Audio device enumeration failed");
                }
                try { _endpoint.audDevManager().setNullDev(); }
                catch (Exception ex) { _logger.LogError(ex, "setNullDev failed"); }
            }

            _endpoint.libStart();

            // Eagerly register the .NET GC Finalizer thread with pjlib before any
            // SWIG wrapper can be collected. Non-subclassed native wrappers
            // (AudioMediaRecorder, ToneGenerator, AudioMedia, CallInfo …) carry
            // finalizers we cannot suppress; if collected on an unregistered
            // thread their destructors abort the process via pj_thread_this().
            // The sentinel's finalizer runs once on that thread and registers it.
            _ = new FinalizerThreadRegistrar(this);

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

            // The ids belong to the destroyed endpoint; a later re-init issues fresh ones and a
            // stale id would pin an account to a transport that no longer exists.
            _transportIds.Clear();

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

        if (transportOpt.Tls is { } tls)
        {
            if (IsTlsTransport(transportOpt.Type))
                ApplyTlsConfig(cfg, tls);
            else
                _logger.LogWarning(
                    "TLS options supplied for a {TransportType} transport — ignored (they only apply to TLS/TLS6)",
                    transportOpt.Type);
        }

        try
        {
            // The returned id is what an account puts in sipConfig.transportId to pin itself to
            // this transport. Discarding it (as this did until now) leaves every account on
            // PJSUA_INVALID_ID, which makes pjsua_init_tpselector() a no-op — so the transport of
            // every out-of-dialog request is chosen from the target URI alone, defaulting to UDP.
            var id = _endpoint!.transportCreate(pjType, cfg);
            _transportIds[transportOpt.Type] = id;
            _logger.LogInformation("Created {TransportType} transport on port {Port} (id={Id})",
                transportOpt.Type, transportOpt.Port, id);
        }
        catch (Exception ex)
        {
            throw new SipTransportException(
                $"Failed to create {transportOpt.Type} transport on port {transportOpt.Port}", ex);
        }
    }

    private static bool IsTlsTransport(SipTransportType type) =>
        type is SipTransportType.Tls or SipTransportType.Tls6;

    /// <summary>
    /// Copies <see cref="TlsOptions"/> onto the native <c>TransportConfig.tlsConfig</c>.
    ///
    /// These settings used to be accepted and silently dropped: nothing ever read
    /// <see cref="SipTransportOptions.Tls"/>, so a caller asking for certificate verification got
    /// pjsua2's default of <c>verifyServer = false</c> and an unverified TLS connection while its
    /// own configuration claimed otherwise.
    ///
    /// Trust anchors differ by platform, and that decides whether <c>VerifyServer</c> works with no
    /// CA list (see native/config_site.h):
    /// <list type="bullet">
    /// <item>macOS/iOS — Apple backend: <c>SecTrustEvaluateWithError</c> against the SYSTEM trust
    /// store, so a publicly-trusted server certificate verifies with no CA list. Supplying
    /// <see cref="TlsOptions.CaListFile"/> calls <c>SecTrustSetAnchorCertificatesOnly</c>, which
    /// REPLACES the system anchors — use it only for a private CA.</item>
    /// <item>Windows — Schannel: system trust store, same as above.</item>
    /// <item>Android — OpenSSL: PJSIP loads anchors ONLY from CA_file/CA_path/CA_buf and never
    /// calls <c>SSL_CTX_set_default_verify_paths()</c>, so with no CA list the trust store is
    /// EMPTY and every handshake fails. Hence the warning below.</item>
    /// </list>
    /// </summary>
    private void ApplyTlsConfig(TransportConfig cfg, TlsOptions tls)
    {
        // SWIG returns a wrapper over the native member (cMemoryOwn=false), so writes go straight
        // into the struct — no re-assignment back onto cfg is needed.
        var tlsCfg = cfg.tlsConfig;

        tlsCfg.verifyServer = tls.VerifyServer;
        tlsCfg.verifyClient = tls.VerifyClient;

        if (!string.IsNullOrWhiteSpace(tls.CaListFile))
            tlsCfg.CaListFile = tls.CaListFile;
        if (!string.IsNullOrWhiteSpace(tls.CertificateFile))
            tlsCfg.certFile = tls.CertificateFile;
        if (!string.IsNullOrWhiteSpace(tls.PrivateKeyFile))
            tlsCfg.privKeyFile = tls.PrivateKeyFile;

        _logger.LogInformation(
            "TLS config: verifyServer={VerifyServer}, verifyClient={VerifyClient}, caList={CaList}, clientCert={ClientCert}",
            tls.VerifyServer, tls.VerifyClient,
            string.IsNullOrWhiteSpace(tls.CaListFile) ? "(system trust store)" : tls.CaListFile,
            string.IsNullOrWhiteSpace(tls.CertificateFile) ? "(none)" : tls.CertificateFile);

        if (tls.VerifyServer && string.IsNullOrWhiteSpace(tls.CaListFile) && OperatingSystem.IsAndroid())
        {
            _logger.LogWarning(
                "TLS verifyServer is ON with no CaListFile on Android. PJSIP uses OpenSSL there and " +
                "loads trust anchors ONLY from a CA list — the system store is NOT consulted, so every " +
                "TLS handshake will fail. Supply TlsOptions.CaListFile with a PEM bundle, or turn " +
                "verification off on this platform.");
        }

        if (!tls.VerifyServer)
        {
            _logger.LogWarning(
                "TLS server certificate verification is DISABLED — the connection is encrypted but the " +
                "server's identity is not authenticated (vulnerable to an active man-in-the-middle).");
        }
    }

    /// <summary>
    /// Looks up the pjsua transport id for a transport type, so an account can pin itself to it via
    /// <c>AccountConfig.sipConfig.transportId</c>.
    /// </summary>
    /// <returns>False when no transport of that type was created — the caller must then leave the
    /// account on PJSUA_INVALID_ID rather than guess, and should say so in the log.</returns>
    internal bool TryGetTransportId(SipTransportType type, out int transportId) =>
        _transportIds.TryGetValue(type, out transportId);

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

    /// <summary>
    /// Registers the current OS thread with pjlib if it is not already known.
    /// pjsua2 APIs (including the native destructors invoked by SWIG when a
    /// wrapper is finalized) call <c>pj_thread_this()</c>, which aborts the
    /// whole process via <c>pj_assert</c> when run on a thread pjlib has never
    /// seen — e.g. the .NET GC Finalizer thread. This is a best-effort safety
    /// net; the primary defence is suppressing the SWIG finalizers and
    /// marshalling native disposal onto the PJSIP worker thread.
    /// </summary>
    internal void EnsureThreadRegistered(string threadName)
    {
        var endpoint = _endpoint;
        if (endpoint is null) return;

        try
        {
            if (endpoint.libIsThreadRegistered()) return;
            endpoint.libRegisterThread(threadName);
            _logger.LogDebug("Registered thread '{ThreadName}' with pjlib", threadName);
        }
        catch (Exception ex)
        {
            // Registration is itself a pjsua2 call; if pjlib is mid-shutdown it
            // may throw. Swallow — the caller is already on a best-effort path.
            _logger.LogDebug(ex, "Could not register thread '{ThreadName}' with pjlib", threadName);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _invoker.Dispose();
    }

    /// <summary>
    /// Throwaway object whose finalizer runs on the .NET GC Finalizer thread and
    /// registers that thread with pjlib. It re-arms (re-registers itself for
    /// finalization) so the registration is refreshed if pjlib is ever torn down
    /// and recreated, and so it keeps the finalizer thread known to pjlib for the
    /// process lifetime. Stops re-arming once the endpoint manager is disposed.
    /// </summary>
    private sealed class FinalizerThreadRegistrar
    {
        private readonly PjSipEndpointManager _owner;

        public FinalizerThreadRegistrar(PjSipEndpointManager owner) => _owner = owner;

        ~FinalizerThreadRegistrar()
        {
            if (_owner._disposed) return;

            _owner.EnsureThreadRegistered("GC-Finalizer");

            // Re-arm so the finalizer thread stays registered across future GCs
            // and endpoint re-inits. GC.ReRegisterForFinalize requeues this same
            // instance; keep cycling a fresh sentinel to avoid edge cases with
            // re-registration on a resurrected object.
            try { _ = new FinalizerThreadRegistrar(_owner); }
            catch { /* shutting down */ }
        }
    }
}
