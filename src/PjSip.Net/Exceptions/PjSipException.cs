namespace PjSip.Net.Exceptions;

public class PjSipException : Exception
{
    public int PjStatusCode { get; }

    public PjSipException(string message) : base(message) { }

    public PjSipException(string message, int pjStatusCode)
        : base(message)
    {
        PjStatusCode = pjStatusCode;
    }

    public PjSipException(string message, Exception innerException)
        : base(message, innerException) { }

    public PjSipException(string message, int pjStatusCode, Exception innerException)
        : base(message, innerException)
    {
        PjStatusCode = pjStatusCode;
    }
}
