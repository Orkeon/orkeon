namespace Orkeon.Tools.Code.Constants.Shell;

/// <summary>
/// Default values for shell command execution operations.
/// </summary>
internal static class ShellDefaults
{
    /// <summary>Default timeout in seconds for shell command execution.</summary>
    public const int DefaultTimeoutSeconds = 30;

    /// <summary>Windows shell executable used for built-in commands.</summary>
    public const string WindowsShell = "cmd.exe";

    /// <summary>Command switch passed to the Windows shell to execute a command string.</summary>
    public const string WindowsCommandSwitch = "/c";
}
