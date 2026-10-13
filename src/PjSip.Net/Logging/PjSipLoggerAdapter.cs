using Microsoft.Extensions.Logging;
using PjSip.Net.Interop.Generated;

namespace PjSip.Net.Logging;

internal sealed class PjSipLoggerAdapter
{
    // A native fault can make PJSIP emit the same line in a tight loop (iOS UDP transport on a
    // reclaimed socket: 128k identical lines in ~5 min, 50 MB log files rolling). Consecutive
    // repeats are collapsed: one line per RepeatSummaryEvery, plus a final count when it stops.
    private const int RepeatSummaryEvery = 1000;

    private readonly ILogger _logger;
    private readonly object _repeatGate = new();
    private string? _lastText;
    private int _lastLevel;
    private int _repeats;
    private PjSipLogWriter? _logWriter;

    public PjSipLoggerAdapter(ILogger<PjSipLoggerAdapter> logger)
    {
        _logger = logger;
    }

    public void OnLog(int level, string message, int threadId)
    {
        var text = message.TrimEnd();
        // PJSIP prefixes every line with its own "HH:mm:ss.fff" — ignore it when comparing.
        var key = text.Length > 12 && text[2] == ':' && text[8] == '.' ? text[12..].TrimStart() : text;

        lock (_repeatGate)
        {
            if (level == _lastLevel && key == _lastText)
            {
                if (++_repeats % RepeatSummaryEvery == 0)
                    Write(level, threadId, $"(repeated {RepeatSummaryEvery} more times) {text}");
                return;
            }

            if (_repeats % RepeatSummaryEvery != 0)
                Write(_lastLevel, threadId, $"(previous message repeated {_repeats % RepeatSummaryEvery} more times)");

            _lastText = key;
            _lastLevel = level;
            _repeats = 0;
            Write(level, threadId, text);
        }
    }

    private void Write(int level, int threadId, string text)
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

        _logger.Log(logLevel, "[PJSIP T{ThreadId}] {Message}", threadId, text);
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
