using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PjSip.Net.Exceptions;
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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) return;

        await _invoker.InvokeAsync(() =>
        {
            _logger.LogInformation("Initializing PJSIP endpoint (UserAgent={UserAgent})", _options.UserAgent);

            // TODO: When SWIG-generated classes are available:
            // 1. Create pj.Endpoint instance
            // 2. Call ep.libCreate()
            // 3. Configure EpConfig (ua.userAgent, log level, etc.)
            // 4. Register log writer callback via _loggerAdapter
            // 5. Call ep.libInit(epConfig)
            // 6. Create transports from _options.Transports
            // 7. Call ep.libStart()

            _initialized = true;
            _logger.LogInformation("PJSIP endpoint initialized successfully");
        });
    }

    public async Task ShutdownAsync()
    {
        if (!_initialized || _disposed) return;

        await _invoker.InvokeAsync(() =>
        {
            _logger.LogInformation("Shutting down PJSIP endpoint");

            // TODO: When SWIG-generated classes are available:
            // 1. Hangup all calls
            // 2. Unregister all accounts
            // 3. Call ep.libDestroy()

            _initialized = false;
            _logger.LogInformation("PJSIP endpoint shut down");
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _invoker.Dispose();
    }
}
