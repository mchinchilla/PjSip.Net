namespace PjSip.Net.Exceptions;

public sealed class SipRegistrationException : PjSipException
{
    public int SipStatusCode { get; }

    public SipRegistrationException(string message, int sipStatusCode)
        : base(message)
    {
        SipStatusCode = sipStatusCode;
    }

    public SipRegistrationException(string message, int sipStatusCode, Exception innerException)
        : base(message, innerException)
    {
        SipStatusCode = sipStatusCode;
    }
}
