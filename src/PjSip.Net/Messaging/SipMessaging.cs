using Microsoft.Extensions.Logging;
using PjSip.Net.Accounts;
using PjSip.Net.Internal;
using PjSip.Net.Interop.Generated;

namespace PjSip.Net.Messaging;

internal sealed class SipMessaging : ISipMessaging
{
    private readonly PjSipEndpointManager _endpointManager;
    private readonly ILogger _logger;

    public SipMessaging(
        PjSipEndpointManager endpointManager,
        ILogger logger)
    {
        _endpointManager = endpointManager;
        _logger = logger;
    }

    public event EventHandler<SipMessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<SipMessageStatusEventArgs>? MessageStatus;
    public event EventHandler<SipTypingIndicationEventArgs>? TypingIndicationReceived;

    public async Task SendMessageAsync(ISipAccount account, string destinationUri, string body, string contentType = "text/plain", CancellationToken ct = default)
    {
        if (account is SipAccount sipAccount && sipAccount.Inner.Native is not null)
        {
            await _endpointManager.Invoker.InvokeAsync(() =>
            {
                _logger.LogInformation("Sending SIP MESSAGE from {Account} to {Destination}", account.Uri, destinationUri);

                // Create a temporary buddy to send the instant message
                using var buddy = new Buddy();
                using var buddyCfg = new BuddyConfig();
                buddyCfg.uri = destinationUri;
                buddyCfg.subscribe = false;
                buddy.create(sipAccount.Inner.Native, buddyCfg);

                using var prm = new SendInstantMessageParam();
                prm.content = body;
                prm.contentType = contentType;
                buddy.sendInstantMessage(prm);
            });
        }
        else
        {
            _logger.LogInformation("Sending SIP MESSAGE from {Account} to {Destination} (stub mode)", account.Uri, destinationUri);
        }
    }

    public async Task SendTypingIndicationAsync(ISipAccount account, string destinationUri, bool isTyping, CancellationToken ct = default)
    {
        if (account is SipAccount sipAccount && sipAccount.Inner.Native is not null)
        {
            await _endpointManager.Invoker.InvokeAsync(() =>
            {
                _logger.LogDebug("Sending typing indication ({IsTyping}) from {Account} to {Destination}",
                    isTyping, account.Uri, destinationUri);

                using var buddy = new Buddy();
                using var buddyCfg = new BuddyConfig();
                buddyCfg.uri = destinationUri;
                buddyCfg.subscribe = false;
                buddy.create(sipAccount.Inner.Native, buddyCfg);

                using var prm = new SendTypingIndicationParam();
                prm.isTyping = isTyping;
                buddy.sendTypingIndication(prm);
            });
        }
    }

    internal void OnTypingIndication(string fromUri, bool isTyping)
    {
        TypingIndicationReceived?.Invoke(this, new SipTypingIndicationEventArgs
        {
            FromUri = fromUri,
            IsTyping = isTyping
        });
    }

    internal void OnMessageReceived(SipMessage message)
    {
        MessageReceived?.Invoke(this, new SipMessageReceivedEventArgs { Message = message });
    }

    internal void OnMessageStatus(string destinationUri, int statusCode, string? reason)
    {
        MessageStatus?.Invoke(this, new SipMessageStatusEventArgs
        {
            DestinationUri = destinationUri,
            StatusCode = statusCode,
            Reason = reason
        });
    }
}
