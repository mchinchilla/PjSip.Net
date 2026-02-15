using Microsoft.Extensions.Logging;
using PjSip.Net.Interop.Generated;

namespace PjSip.Net.Logging;

internal sealed class PjSipLoggerAdapter
{
    private readonly ILogger _logger;
    private PjSipLogWriter? _logWriter;

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

    /// <summary>
    /// Returns a SWIG LogWriter that forwards pjsua2 log entries to this adapter.
    /// The instance is created lazily and reused.
    /// </summary>
    internal LogWriter GetOrCreateLogWriter()
    {
        _logWriter ??= new PjSipLogWriter(this);
        return _logWriter;
    }

    /// <summary>
    /// SWIG Director subclass that receives native log callbacks from pjsua2
    /// and delegates them to the managed PjSipLoggerAdapter.
    /// </summary>
    private sealed class PjSipLogWriter : LogWriter
    {
        private readonly PjSipLoggerAdapter _adapter;

        public PjSipLogWriter(PjSipLoggerAdapter adapter)
        {
            _adapter = adapter;
        }

        public override void write(LogEntry entry)
        {
            try
            {
                _adapter.OnLog(entry.level, entry.msg, entry.threadId);
            }
            catch
            {
                // Never let a logging failure propagate back into native code.
            }
        }
    }
}
