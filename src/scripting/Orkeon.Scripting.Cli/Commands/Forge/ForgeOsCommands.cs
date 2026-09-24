using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// What one operating-system command answered: its exit code and its two streams — or that it
/// never started, the binary being absent (no <c>crontab</c> on the machine, no <c>systemctl</c>).
/// </summary>
/// <param name="Started">Whether the process started at all.</param>
/// <param name="ExitCode">The exit code; -1 when it never started or was stopped on its timeout.</param>
/// <param name="StandardOutput">What it wrote on stdout.</param>
/// <param name="StandardError">What it wrote on stderr — or why it never started.</param>
internal sealed record ForgeOsCommandResult(bool Started, int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>A command whose binary could not be started.</summary>
    public static ForgeOsCommandResult NotStarted(string reason) => new(false, -1, "", reason);

    /// <summary>Whether the command ran and exited 0.</summary>
    public bool Succeeded => Started && ExitCode == 0;

    /// <summary>The first thing the command said about itself: stderr, else stdout, else its exit code.</summary>
    public string Diagnostic
    {
        get
        {
            if (StandardError.Trim() is { Length: > 0 } error)
                return error;
            if (StandardOutput.Trim() is { Length: > 0 } output)
                return output;
            return Started ? $"exit code {ExitCode}" : "the command could not be started";
        }
    }
}

/// <summary>
/// The one seam through which the schedule verbs reach the operating system (STUDIO-27, D-04):
/// a binary and its arguments as a LIST — never a command line, never a shell — and, for
/// <c>crontab -</c>, the text to write on its stdin. The tests put a hand-written double here;
/// nothing under test ever runs <c>schtasks</c>, <c>systemctl</c> or <c>crontab</c>.
/// </summary>
internal interface IForgeOsCommands
{
    /// <summary>Runs <paramref name="fileName"/> with <paramref name="arguments"/>, feeding <paramref name="standardInput"/> when given.</summary>
    ForgeOsCommandResult Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null);
}

/// <summary>
/// The real machine: <see cref="Process"/> with <see cref="ProcessStartInfo.ArgumentList"/> and
/// no shell (<see cref="ProcessStartInfo.UseShellExecute"/> false), so a team folder holding a
/// space, a quote or a <c>&amp;</c> reaches the OS as the one argument it is. No elevation is
/// ever asked: every command acts on the current user's own scheduler.
/// </summary>
internal sealed class SystemForgeOsCommands : IForgeOsCommands
{
    /// <summary>How long a scheduler command may take before it is stopped.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>The shared, stateless instance.</summary>
    public static SystemForgeOsCommands Instance { get; } = new();

    /// <inheritdoc />
    public ForgeOsCommandResult Run(string fileName, IReadOnlyList<string> arguments, string? standardInput = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        // No CreateNoWindow: the child shares this process's console, so it writes in the code
        // page this process reads it back in (orkeon switches its console to UTF-8 at start),
        // and a console-less launch from Studio flashes no window either — the console it
        // shares is Studio's hidden one.
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        if (standardInput is not null)
            startInfo.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception ex)
        {
            return ForgeOsCommandResult.NotStarted($"'{fileName}' could not be started: {ex.Message}");
        }

        if (process is null)
            return ForgeOsCommandResult.NotStarted($"'{fileName}' could not be started.");

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (standardInput is not null)
            {
                process.StandardInput.Write(standardInput);
                process.StandardInput.Close();
            }

            if (!process.WaitForExit(Timeout))
            {
                StopQuietly(process);
                return new ForgeOsCommandResult(
                    true, -1, "", $"'{fileName}' did not answer within {Timeout.TotalSeconds:0} seconds and was stopped.");
            }

            // The parameterless wait also drains the redirected streams to their end.
            process.WaitForExit();
            return new ForgeOsCommandResult(
                true, process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }
    }

    private static void StopQuietly(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // It exited between the timeout and the kill: nothing left to stop.
        }
    }
}
