using PjSip.Net.Accounts;

namespace PjSip.Net.Messaging;

public interface ISipMessaging
{
    Task SendMessageAsync(ISipAccount account, string destinationUri, string body, string contentType = "text/plain", CancellationToken ct = default);
    Task SendTypingIndicationAsync(ISipAccount account, string destinationUri, bool isTyping, CancellationToken ct = default);
    event EventHandler<SipMessageReceivedEventArgs>? MessageReceived;
    event EventHandler<SipMessageStatusEventArgs>? MessageStatus;
    event EventHandler<SipTypingIndicationEventArgs>? TypingIndicationReceived;
}
