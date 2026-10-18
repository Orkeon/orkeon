namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary>Finds processes through <c>/proc</c> (Linux), to prove what a session leaves behind.</summary>
internal static class LinuxProcesses
{
    /// <summary>
    /// A duration no other process uses, so a <c>sleep</c> is found by its command line alone even
    /// when several test passes run at once.
    /// </summary>
    public static string UniqueSleepDuration() => $"3600.{Random.Shared.Next(100_000, 999_999)}";

    /// <summary>The pid of a living process whose arguments end with <paramref name="arguments"/>, or null.</summary>
    public static int? Find(params string[] arguments)
    {
        var expected = string.Concat(arguments.Select(argument => argument + "\0"));
        return Find(cmdline => cmdline.EndsWith(expected, StringComparison.Ordinal));
    }

    /// <summary>The pid of the watchdog a session started on <paramref name="hostPid"/> for <paramref name="sessionPid"/>, or null.</summary>
    public static int? FindWatchdog(int hostPid, int sessionPid)
        => Find(cmdline => cmdline.Contains($"\0{Orkeon.Tools.Code.Sessions.ShellScript.WatchdogName}\0{hostPid}\0{sessionPid}\0", StringComparison.Ordinal));

    private static int? Find(Func<string, bool> matches)
    {
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
            try
            {
                var cmdline = File.ReadAllText(Path.Combine(dir, "cmdline"));
                if (matches(cmdline) && IsAlive(pid))
                    return pid;
            }
            catch (IOException) { /* the process exited while being read */ }
            catch (UnauthorizedAccessException) { /* another user's process */ }
        }
        return null;
    }

    /// <summary>Whether the process exists and is not a zombie waiting to be reaped.</summary>
    public static bool IsAlive(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            // The state follows the command name, which is parenthesised and may hold spaces.
            var state = stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[0];
            return state != "Z" && state != "X";
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Kills a process a failing test would otherwise leak.</summary>
    public static void KillIfAlive(int? pid)
    {
        if (pid is not { } leftover || !IsAlive(leftover)) return;
        try
        {
            System.Diagnostics.Process.GetProcessById(leftover).Kill();
        }
        catch (ArgumentException) { /* exited meanwhile */ }
        catch (InvalidOperationException) { /* exited meanwhile */ }
    }
}
