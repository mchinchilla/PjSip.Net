namespace PjSip.Net.Exceptions;

public sealed class SipTransportException : PjSipException
{
    public SipTransportException(string message) : base(message) { }

    public SipTransportException(string message, int pjStatusCode)
        : base(message, pjStatusCode) { }

    public SipTransportException(string message, Exception innerException)
        : base(message, innerException) { }
}
