using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PjSip.Net.Accounts;
using PjSip.Net.Calls;
using PjSip.Net.Conference;
using PjSip.Net.Events;
using PjSip.Net.History;
using PjSip.Net.Internal;
using PjSip.Net.Media;
using PjSip.Net.Messaging;
using PjSip.Net.Network;
using PjSip.Net.Presence;
using PjSip.Net.Quality;
using PjSip.Net.Recording;
using PjSip.Net.Tones;

namespace PjSip.Net;

internal sealed class SipPhone : ISipPhone
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly SipPhoneOptions _options;
    private readonly ILogger<SipPhone> _logger;
    private readonly List<SipAccount> _accounts = [];
    private readonly SipAudioManager _audioManager;
    private readonly SipCodecManager _codecManager;
    private readonly SipPresenceManager _presenceManager;
    private readonly SipMessaging _messaging;
    private readonly SipConferenceBridge _conferenceBridge;
    private readonly SipCallRecorder _callRecorder;
    private readonly SipToneGenerator _toneGenerator;
    private readonly SipCallQualityMonitor _qualityMonitor;
    private readonly SipCallHistory _callHistory;
    private readonly SipNetworkMonitor _networkMonitor;
    private readonly object _lock = new();
    private volatile bool _disposed;

    public SipPhone(
        PjSipEndpointManager endpointManager,
        IOptions<SipPhoneOptions> options,
        ILogger<SipPhone> logger)
    {
        _endpointManager = endpointManager;
        _options = options.Value;
        _logger = logger;

        _audioManager = new SipAudioManager(endpointManager);
        _codecManager = new SipCodecManager(endpointManager, logger);
        _presenceManager = new SipPresenceManager(endpointManager, logger);
        _messaging = new SipMessaging(endpointManager, logger);
        _conferenceBridge = new SipConferenceBridge(endpointManager, logger);
        _callRecorder = new SipCallRecorder(endpointManager, logger);
        _toneGenerator = new SipToneGenerator(endpointManager, logger);
        _qualityMonitor = new SipCallQualityMonitor(endpointManager, logger);
        _callHistory = new SipCallHistory(_options.CallHistoryMaxEntries);
        _networkMonitor = new SipNetworkMonitor(endpointManager, logger);

        // Wire transport state events from endpoint manager
        _endpointManager.TransportStateChanged += (s, e) => TransportStateChanged?.Invoke(this, e);
    }

    public SipPhoneState State { get; private set; } = SipPhoneState.Idle;

    public IReadOnlyList<ISipAccount> Accounts
    {
        get { lock (_lock) return _accounts.ToArray(); }
    }

    public ISipAudioManager Audio => _audioManager;
    public ISipCodecManager Codecs => _codecManager;
    public ISipPresenceManager Presence => _presenceManager;
    public ISipMessaging Messaging => _messaging;
    public ISipConferenceBridge Conference => _conferenceBridge;
    public ISipCallRecorder Recorder => _callRecorder;
    public ISipToneGenerator Tones => _toneGenerator;
    public ISipCallQualityMonitor Quality => _qualityMonitor;
    public ISipCallHistory History => _callHistory;
    public ISipNetworkMonitor Network => _networkMonitor;

    public event EventHandler<IncomingCallEventArgs>? IncomingCall;
    public event EventHandler<CallStateChangedEventArgs>? CallStateChanged;
    public event EventHandler<RegistrationStateChangedEventArgs>? RegistrationStateChanged;
    public event EventHandler<TransportStateChangedEventArgs>? TransportStateChanged;
    public event EventHandler<MwiStateChangedEventArgs>? MwiStateChanged;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State == SipPhoneState.Running) return;

        State = SipPhoneState.Starting;
        _logger.LogInformation("Starting SipPhone");

        try
        {
            await _endpointManager.InitializeAsync(cancellationToken);

            // Auto-register accounts from options
            foreach (var accountOptions in _options.Accounts)
            {
                var account = AddAccountInternal(accountOptions);
                if (accountOptions.RegisterOnAdd)
                {
                    await account.RegisterAsync(cancellationToken);
                }
            }

            _networkMonitor.StartMonitoring();

            State = SipPhoneState.Running;
            _logger.LogInformation("SipPhone started with {AccountCount} account(s)", _accounts.Count);
        }
        catch (Exception ex)
        {
            State = SipPhoneState.Error;
            _logger.LogError(ex, "Failed to start SipPhone");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (State is SipPhoneState.Stopped or SipPhoneState.Idle) return;

        State = SipPhoneState.Stopping;
        _logger.LogInformation("Stopping SipPhone");

        try
        {
            // Dispose sub-managers that hold native resources
            _callRecorder.Dispose();
            _toneGenerator.Dispose();
            _networkMonitor.Dispose();

            // Dispose all buddies via presence manager
            foreach (var buddy in _presenceManager.Buddies.ToArray())
            {
                _presenceManager.RemoveBuddy(buddy);
            }

            // Dispose all accounts (and their active calls)
            lock (_lock)
            {
                foreach (var account in _accounts)
                {
                    account.Dispose();
                }
                _accounts.Clear();
            }

            await _endpointManager.ShutdownAsync();
            State = SipPhoneState.Stopped;
            _logger.LogInformation("SipPhone stopped");
        }
        catch (Exception ex)
        {
            State = SipPhoneState.Error;
            _logger.LogError(ex, "Error stopping SipPhone");
            throw;
        }
    }

    public ISipAccount AddAccount(SipAccountOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return AddAccountInternal(options);
    }

    public void RemoveAccount(ISipAccount account)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            if (account is SipAccount sipAccount && _accounts.Remove(sipAccount))
            {
                sipAccount.RegistrationStateChanged -= OnAccountRegistrationStateChanged;
                sipAccount.IncomingCall -= OnAccountIncomingCall;
                sipAccount.MwiStateChanged -= OnAccountMwiStateChanged;
                _presenceManager.RemoveAccount(sipAccount.Inner);
                sipAccount.Dispose();
            }
        }
    }

    public ISipCall MakeCall(ISipAccount account, string destinationUri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogInformation("Making call from {AccountUri} to {Destination}", account.Uri, destinationUri);
        var call = account.MakeCall(destinationUri);
        call.StateChanged += OnCallStateChanged;
        return call;
    }

    public ISipCall MakeCall(ISipAccount account, string destinationUri, IEnumerable<SipHeader> headers)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogInformation("Making call from {AccountUri} to {Destination} with custom headers", account.Uri, destinationUri);
        var call = account.MakeCall(destinationUri, headers);
        call.StateChanged += OnCallStateChanged;
        return call;
    }

    private SipAccount AddAccountInternal(SipAccountOptions options)
    {
        var managed = new ManagedAccount(options, _endpointManager, _logger);
        managed.SetMessaging(_messaging);
        var account = new SipAccount(managed);
        account.RegistrationStateChanged += OnAccountRegistrationStateChanged;
        account.IncomingCall += OnAccountIncomingCall;
        account.MwiStateChanged += OnAccountMwiStateChanged;

        // Wire presence manager to this account
        _presenceManager.AddAccount(managed);

        lock (_lock) _accounts.Add(account);
        return account;
    }

    private void OnAccountRegistrationStateChanged(object? sender, RegistrationStateChangedEventArgs e) =>
        RegistrationStateChanged?.Invoke(this, e);

    private void OnAccountIncomingCall(object? sender, IncomingCallEventArgs e) =>
        IncomingCall?.Invoke(this, e);

    private void OnAccountMwiStateChanged(object? sender, MwiStateChangedEventArgs e) =>
        MwiStateChanged?.Invoke(this, e);

    private void OnCallStateChanged(object? sender, CallStateChangedEventArgs e)
    {
        CallStateChanged?.Invoke(this, e);
        if (e.NewState == SipCallState.Disconnected && sender is ISipCall call)
        {
            call.StateChanged -= OnCallStateChanged;

            // Record in call history
            _callHistory.AddEntry(new CallHistoryEntry
            {
                CallId = call.Id,
                RemoteUri = call.Info.RemoteUri,
                RemoteDisplayName = call.Info.RemoteDisplayName,
                Direction = call.Direction,
                StartTime = DateTime.UtcNow - call.Info.Duration,
                EndTime = DateTime.UtcNow,
                Duration = call.Info.Duration,
                FinalState = e.NewState,
                StatusCode = call.Info.StatusCode
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
        _endpointManager.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
        _endpointManager.Dispose();
    }
}
