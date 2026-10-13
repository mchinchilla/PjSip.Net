using FluentAssertions;
using Microsoft.Extensions.Logging;
using PjSip.Net.Logging;

namespace PjSip.Net.Tests.Unit;

/// <summary>
/// Tests for collapsing a native log line that PJSIP repeats in a tight loop (iOS UDP transport on
/// a reclaimed socket logged 128k identical lines in minutes). Lines differing only in PJSIP's own
/// "HH:mm:ss.fff" prefix count as repeats; anything else must still be logged.
/// </summary>
public class PjSipLoggerAdapterTests
{
    private const string LoopLine = "udp0x12c777400  Warning: pj_ioqueue_recvfrom: [err 120057] Socket is not connected";

    [Fact]
    public void OnLog_RepeatedLine_CollapsedToOneLinePerThousand()
    {
        var (adapter, lines) = Create();

        for (var i = 0; i < 2500; i++)
            adapter.OnLog(2, $"23:37:38.{i % 1000:000}  {LoopLine}", 1);

        // first occurrence + a summary at 1000 and 2000 repeats (2499 repeats so far)
        lines.Should().HaveCount(3);
        lines[1].Should().Contain("(repeated 1000 more times)");
    }

    [Fact]
    public void OnLog_DifferentLineAfterRepeats_ReportsRemainingCountThenLogsIt()
    {
        var (adapter, lines) = Create();

        for (var i = 0; i < 1500; i++)
            adapter.OnLog(2, $"23:37:38.100  {LoopLine}", 1);
        adapter.OnLog(3, "23:37:40.000  pjsua_acc.c  Registration sent", 1);

        lines.Should().HaveCount(4);
        lines[2].Should().Contain("(previous message repeated 499 more times)");
        lines[3].Should().Contain("Registration sent");
    }

    [Fact]
    public void OnLog_DistinctLines_AllLogged()
    {
        var (adapter, lines) = Create();

        adapter.OnLog(3, "23:37:40.000  a", 1);
        adapter.OnLog(3, "23:37:40.001  b", 1);
        adapter.OnLog(4, "23:37:40.002  b", 1); // same text, different level

        lines.Should().HaveCount(3);
    }

    private static (PjSipLoggerAdapter Adapter, List<string> Lines) Create()
    {
        var lines = new List<string>();
        var logger = new ListLogger(lines);
        return (new PjSipLoggerAdapter(logger), lines);
    }

    private sealed class ListLogger(List<string> lines) : ILogger<PjSipLoggerAdapter>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => lines.Add(formatter(state, exception));
    }
}
