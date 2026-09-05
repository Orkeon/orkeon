using Microsoft.Extensions.Hosting.WindowsServices;

namespace Orkeon.Host;

/// <summary>
/// Where a refused configuration (exit 78) gets reported. In a terminal or under systemd,
/// stderr is enough — the operator or the journal reads it. Under the Windows SCM, stderr
/// goes nowhere: the only place an operator will ever see the message is the Application
/// event log. The routing is decided once from how the process is being supervised.
/// </summary>
internal interface IStartupFailureSink
{
    void Report(string message);
}

/// <summary>Stderr — always present, whatever the supervisor.</summary>
internal sealed class ConsoleFailureSink : IStartupFailureSink
{
    public void Report(string message) => Console.Error.WriteLine(message);
}

/// <summary>
/// The Application event log, source <c>Orkeon</c> (created by install-service.ps1 at
/// registration time — the virtual service account may not create it on first write; the
/// <c>.NET Runtime</c> source is the always-registered fallback). Constructible anywhere so
/// the routing stays a pure, testable function; the platform guard lives in the write.
/// Reporting must never mask the exit code it reports, so every failure here is swallowed.
/// </summary>
internal sealed class EventLogFailureSink : IStartupFailureSink
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "Best-effort reporting of an exit already underway: any event-log failure (missing source, ACL, service registry) must not replace the exit-78 message with a crash.")]
    public void Report(string message)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            var source = System.Diagnostics.EventLog.SourceExists("Orkeon") ? "Orkeon" : ".NET Runtime";
            System.Diagnostics.EventLog.WriteEntry(source, message, System.Diagnostics.EventLogEntryType.Error);
        }
        catch
        {
            // The one message that matters is the exit 78 already on its way out; a logging
            // failure must not replace it with a crash.
        }
    }
}

/// <summary>
/// The single exit point for every pre-host configuration refusal in Program.cs — five paths
/// used to write to stderr individually, and all five were invisible under the SCM.
/// </summary>
internal static class StartupFailureReporter
{
    private static IReadOnlyList<IStartupFailureSink> _sinks = For(WindowsServiceHelpers.IsWindowsService());

    /// <summary>Swapped by tests; initialized from the real supervisor otherwise.</summary>
    internal static IReadOnlyList<IStartupFailureSink> Sinks
    {
        get => _sinks;
        set => _sinks = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Pure routing, testable everywhere: stderr alone in a terminal or under systemd,
    /// stderr plus the event log under the Windows SCM.
    /// </summary>
    internal static IReadOnlyList<IStartupFailureSink> For(bool isWindowsService) =>
        isWindowsService
            ? [new ConsoleFailureSink(), new EventLogFailureSink()]
            : [new ConsoleFailureSink()];

    internal static void Report(string message)
    {
        foreach (var sink in _sinks)
            sink.Report(message);
    }
}
