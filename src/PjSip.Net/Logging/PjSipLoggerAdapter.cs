using Microsoft.Extensions.Logging;

namespace PjSip.Net.Logging;

internal sealed class PjSipLoggerAdapter
{
    private readonly ILogger _logger;

    public PjSipLoggerAdapter(ILogger<PjSipLoggerAdapter> logger)
    {
        _logger = logger;
    }

    public void OnLog(int level, string message, int threadId)
    {
        var logLevel = level switch
        {
            0 => LogLevel.Critical,
            1 => LogLevel.Error,
            2 => LogLevel.Warning,
            3 => LogLevel.Information,
            4 => LogLevel.Debug,
            _ => LogLevel.Trace
        };

        _logger.Log(logLevel, "[PJSIP T{ThreadId}] {Message}", threadId, message.TrimEnd());
    }
}
