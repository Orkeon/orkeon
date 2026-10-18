using System.Diagnostics;
using System.Globalization;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary>
/// Finds processes through <c>ps</c> (macOS has no <c>/proc</c>), to prove what a session leaves behind.
/// </summary>
internal static class PsProcesses
{
    /// <summary>The pid of a living process whose arguments end with <paramref name="arguments"/>, or null.</summary>
    public static int? Find(params string[] arguments)
    {
        var expected = string.Join(' ', arguments);
        foreach (var line in Ps("-A", "-o", "pid=", "-o", "stat=", "-o", "args="))
        {
            var fields = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 3 && !fields[1].StartsWith('Z') && fields[2].EndsWith(expected, StringComparison.Ordinal)
                && int.TryParse(fields[0], CultureInfo.InvariantCulture, out var pid))
                return pid;
        }

        return null;
    }

    /// <summary>Whether the process exists and is not a zombie waiting to be reaped (<c>ps -p</c>).</summary>
    public static bool IsAlive(int pid)
    {
        var state = Ps("-p", pid.ToString(CultureInfo.InvariantCulture), "-o", "stat=").FirstOrDefault()?.Trim();
        return !string.IsNullOrEmpty(state) && !state.StartsWith('Z');
    }

    /// <summary>Kills a process a failing test would otherwise leak.</summary>
    public static void KillIfAlive(int? pid)
    {
        if (pid is not { } leftover || !IsAlive(leftover)) return;
        try
        {
            Process.GetProcessById(leftover).Kill();
        }
        catch (ArgumentException) { /* exited meanwhile */ }
        catch (InvalidOperationException) { /* exited meanwhile */ }
    }

    private static string[] Ps(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("/bin/ps") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
