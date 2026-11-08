using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Application.Runs;

/// <summary>Tells whether the process that was running a run still exists.</summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface IRunProcessProbe
{
    /// <summary>True when a process with this id exists and is the one that started at <paramref name="startedAt"/> (a reused id is another process).</summary>
    bool IsAlive(int pid, DateTimeOffset? startedAt);
}

/// <summary>The process a run was started by, recorded with the run so that a run whose process vanished can be told from one still in flight.</summary>
/// <param name="Pid">The process id.</param>
/// <param name="Host">The machine name: a process can only be probed on its own host.</param>
/// <param name="StartedAt">When the process started, to tell it from a later one that reuses the id.</param>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record RunProcessIdentity(int Pid, string Host, DateTimeOffset? StartedAt)
{
    /// <summary>The current process.</summary>
    public static RunProcessIdentity Current { get; } = ReadCurrent();

    private static RunProcessIdentity ReadCurrent()
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        DateTimeOffset? started = null;
        try
        {
            started = new DateTimeOffset(self.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // A platform that does not tell: the id alone is then what is compared.
        }

        return new RunProcessIdentity(Environment.ProcessId, Environment.MachineName, started);
    }
}

/// <summary>The probe of the machine: a process exists when the system finds it, and is the same when it started when the run said.</summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed class SystemRunProcessProbe : IRunProcessProbe
{
    /// <inheritdoc />
    public bool IsAlive(int pid, DateTimeOffset? startedAt)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            if (process.HasExited)
                return false;
            if (startedAt is not { } expected)
                return true;

            return Math.Abs((new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero) - expected).TotalSeconds) < 5;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // Not allowed to look at it: it is there.
            return true;
        }
    }
}

/// <summary>
/// A run the history holds as <see cref="RunStatus.Running"/> whose process no longer exists on this host was killed: it is
/// reported <see cref="RunStatus.Orphaned"/>. A run recorded on another host stays running, because only its own host can
/// say; a run recorded without a process (an index that lost its line) stays running too.
/// </summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public static class RunLiveness
{
    /// <summary>The summary as it stands now: orphaned when its process vanished.</summary>
    public static RunSummary Apply(RunSummary summary, IRunProcessProbe probe, string localHost)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(probe);

        return summary.Status == RunStatus.Running
            && summary.Pid is { } pid
            && string.Equals(summary.Host, localHost, StringComparison.OrdinalIgnoreCase)
            && !probe.IsAlive(pid, summary.ProcessStartedAt)
                ? summary with { Status = RunStatus.Orphaned }
                : summary;
    }
}
