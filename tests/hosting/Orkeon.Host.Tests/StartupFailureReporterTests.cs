using Orkeon.Host.Tests.Doubles;

namespace Orkeon.Host.Tests;

/// <summary>
/// Where a refused configuration is reported. Five pre-host exit-78 paths used to write to
/// stderr individually — invisible under the Windows SCM. The reporter routes once: stderr
/// alone in a terminal or under systemd, stderr plus the Application event log under the SCM.
/// </summary>
public sealed class StartupFailureReporterTests : IDisposable
{
    private readonly IReadOnlyList<IStartupFailureSink> _originalSinks = StartupFailureReporter.Sinks;

    public void Dispose() => StartupFailureReporter.Sinks = _originalSinks;

    [Fact]
    public void EveryConfiguredSinkReceivesTheFailure()
    {
        var first = new RecordingFailureSink();
        var second = new RecordingFailureSink();
        StartupFailureReporter.Sinks = [first, second];

        StartupFailureReporter.Report("orkeon-host: settings file not found: nope.json");

        Assert.Equal(["orkeon-host: settings file not found: nope.json"], first.Reported);
        Assert.Equal(["orkeon-host: settings file not found: nope.json"], second.Reported);
    }

    [Fact]
    public void TheTerminalRoutingKeepsStderrAlone()
    {
        var sinks = StartupFailureReporter.For(isWindowsService: false);

        var sink = Assert.Single(sinks);
        Assert.IsType<ConsoleFailureSink>(sink);
    }

    [Fact]
    public void TheServiceRoutingAddsTheEventLogSink()
    {
        var sinks = StartupFailureReporter.For(isWindowsService: true);

        Assert.Equal(2, sinks.Count);
        Assert.IsType<ConsoleFailureSink>(sinks[0]);
        Assert.IsType<EventLogFailureSink>(sinks[1]);
    }

    [Fact]
    public void TheEventLogSinkIsInertOffWindows()
    {
        // Off Windows the sink must be a constructible no-op (the routing stays pure); on
        // Windows a real write would need the SCM context the smoke provides — not this test.
        Assert.SkipUnless(!OperatingSystem.IsWindows(), "This asserts the non-Windows no-op path.");

        new EventLogFailureSink().Report("orkeon-host: inert off Windows");
    }
}
