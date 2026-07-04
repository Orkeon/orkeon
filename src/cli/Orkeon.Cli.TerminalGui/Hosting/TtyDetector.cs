namespace Orkeon.Cli.TerminalGui.Hosting;

/// <summary>
/// Detects whether the current process can host a Terminal.Gui session.
/// Returns false in CI (env <c>CI=true</c>), in non-interactive shells
/// (stdout/stdin redirected), or when <c>TERM</c> is unset on Linux.
/// </summary>
public static class TtyDetector
{
    public static bool IsInteractiveTty()
    {
        // Fully qualified — the sibling `Orkeon.Cli.TerminalGui.Console` namespace
        // shadows `System.Console` for unqualified references in this assembly.
        if (System.Console.IsInputRedirected || System.Console.IsOutputRedirected) return false;
        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase)) return false;
        if (OperatingSystem.IsLinux() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TERM"))) return false;
        return true;
    }

    /// <summary>
    /// Resolve the effective <see cref="UiMode"/> given a requested mode.
    /// <c>null</c> = auto: TUI when interactive TTY, Plain otherwise.
    /// </summary>
    public static UiMode ResolveEffectiveMode(UiMode? requested)
    {
        if (requested == UiMode.Plain) return UiMode.Plain;
        if (requested == UiMode.Tui) return UiMode.Tui;
        return IsInteractiveTty() ? UiMode.Tui : UiMode.Plain;
    }
}
