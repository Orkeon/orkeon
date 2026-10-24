using Orkeon.Constants.Configuration;

namespace Orkeon.Cli.TerminalGui.Hosting;

/// <summary>
/// Detects whether the current process can host a Terminal.Gui session.
/// Returns false in CI (env <c>CI=true</c>), in non-interactive shells
/// (stdout/stdin redirected), or when <c>TERM</c> is unset on Linux.
/// </summary>
public static class TtyDetector
{
    /// <summary>Reads the process console (<see cref="SystemTerminalProbe.Instance"/>).</summary>
    public static bool IsInteractiveTty() => IsInteractiveTty(SystemTerminalProbe.Instance);

    /// <summary>Reads the redirection probes of <paramref name="terminal"/>, then the environment.</summary>
    public static bool IsInteractiveTty(ITerminalProbe terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        if (terminal.IsInputRedirected || terminal.IsOutputRedirected) return false;
        if (string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariableNames.ContinuousIntegration), "true", StringComparison.OrdinalIgnoreCase)) return false;
        if (OperatingSystem.IsLinux() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvironmentVariableNames.Terminal))) return false;
        return true;
    }

    /// <summary>
    /// Resolve the effective <see cref="UiMode"/> given a requested mode.
    /// <c>null</c> = auto: TUI when interactive TTY, Plain otherwise.
    /// </summary>
    public static UiMode ResolveEffectiveMode(UiMode? requested)
        => ResolveEffectiveMode(requested, SystemTerminalProbe.Instance);

    /// <summary>
    /// Resolve the effective <see cref="UiMode"/> given a requested mode, probing
    /// <paramref name="terminal"/> when the mode is automatic.
    /// </summary>
    public static UiMode ResolveEffectiveMode(UiMode? requested, ITerminalProbe terminal)
    {
        if (requested == UiMode.Plain) return UiMode.Plain;
        if (requested == UiMode.Tui) return UiMode.Tui;
        return IsInteractiveTty(terminal) ? UiMode.Tui : UiMode.Plain;
    }
}
