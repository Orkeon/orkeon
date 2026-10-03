using System.Diagnostics;
using Xunit;

namespace Orkeon.Tests.Shared.Launchers;

/// <summary>
/// Runs a team's <c>run.sh</c> through a real <c>/bin/sh</c> over a stand-in <c>orkeon</c> that
/// prints each argument followed by a NUL — the one byte an argument cannot hold, so a value
/// carrying a line break comes back whole. Gives the argument vector the real CLI would receive.
/// </summary>
public static class PosixLauncherShell
{
    /// <summary>Whether this machine can run a POSIX launcher.</summary>
    public static bool IsAvailable => !OperatingSystem.IsWindows() && File.Exists("/bin/sh");

    /// <summary>
    /// The arguments <paramref name="launcherPath"/> hands <c>orkeon</c>; the stand-in lives under
    /// <paramref name="scratchDirectory"/>. Fails the test when the launcher exits non-zero.
    /// </summary>
    public static IReadOnlyList<string> Run(string launcherPath, string scratchDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(scratchDirectory);

        var bin = Path.Combine(scratchDirectory, "stub-bin");
        Directory.CreateDirectory(bin);
        var stub = Path.Combine(bin, "orkeon");
        File.WriteAllText(stub, "#!/bin/sh\nfor a in \"$@\"; do printf '%s\\0' \"$a\"; done\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var startInfo = new ProcessStartInfo("/bin/sh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(launcherPath);
        startInfo.Environment["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"the launcher exited {process.ExitCode}: {stderr}");
        Assert.EndsWith("\0", stdout, StringComparison.Ordinal);
        return stdout[..^1].Split('\0');
    }

    /// <summary>
    /// How .NET splits <paramref name="arguments"/> into an argument vector on this machine — the
    /// rules of the Windows C runtime (<c>Process.Unix.cs</c>) —, read back through
    /// <c>/bin/sh</c>: the reference <see cref="CrtArgv"/> is held against. At least one argument.
    /// </summary>
    public static IReadOnlyList<string> DotnetSplit(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo("/bin/sh")
        {
            Arguments = "-c \"printf '%s\\0' \\\"$@\\\"\" sh " + arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        // printf prints its format once even without an argument: never handed an empty vector.
        Assert.True(process.ExitCode == 0, $"/bin/sh exited {process.ExitCode}: {stderr}");
        Assert.EndsWith("\0", stdout, StringComparison.Ordinal);
        return stdout[..^1].Split('\0');
    }
}
