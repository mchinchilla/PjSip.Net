using Microsoft.Extensions.Logging;
using PjSip.Net.Accounts;
using PjSip.Net.Internal;

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

    public async Task SendMessageAsync(ISipAccount account, string destinationUri, string body, string contentType = "text/plain", CancellationToken ct = default)
    {
        await _endpointManager.Invoker.InvokeAsync(() =>
        {
            _logger.LogInformation("Sending SIP MESSAGE from {Account} to {Destination}", account.Uri, destinationUri);

            // TODO: When SWIG-generated classes are available:
            // 1. Create SendInstantMessageParam
            // 2. Set contentType, body
            // 3. Call account.sendInstantMessage(param)
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
