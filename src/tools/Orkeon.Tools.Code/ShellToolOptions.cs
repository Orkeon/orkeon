using System.Collections.ObjectModel;

namespace Orkeon.Tools.Code;

/// <summary>
/// The <c>Orkeon:Tools:Shell</c> section: what <see cref="ShellCommandTool"/> may run. Bound by
/// <c>AddOrkeonCodeTools</c> and validated when a host starts (GAP-40) — an <c>AllowInterpreters</c> the
/// binder cannot convert used to surface when the registry built the tool, an unhandled exception out
/// of <c>--list-tools</c>.
/// </summary>
public sealed class ShellToolOptions
{
    /// <summary>The configuration section: <c>Orkeon:Tools:Shell</c>.</summary>
    public const string SectionName = "Orkeon:Tools:Shell";

    /// <summary>
    /// Enables the interpreters (<c>node</c>, <c>dotnet</c>, <c>npm</c>, <c>find</c>) and the mutating git
    /// subcommands — RCE-equivalent on the host, for trusted coding-agent hosts only. Default false.
    /// </summary>
    public bool AllowInterpreters { get; set; }

    /// <summary>
    /// A full replacement of the default allowlist, verbatim; it cancels
    /// <see cref="AllowInterpreters"/> and re-enables the git read-only restriction. Empty keeps the
    /// default allowlist.
    /// </summary>
    public Collection<string> AllowedCommands { get; } = [];

    /// <summary>Commands added on top of the default (or replacement) allowlist, such as <c>make</c>.</summary>
    public Collection<string> ExtraAllowedCommands { get; } = [];

    /// <summary>How the process is confined; the backend of the platform by default (<c>Auto</c>).</summary>
    public ShellConfinementOptions Confinement { get; } = new();

    /// <summary>The environment variables added to a session's allow-list (<c>Environment:Propagate</c>).</summary>
    public ShellEnvironmentOptions Environment { get; } = new();

    /// <summary>
    /// Under WSL, the Windows spelling of a mount's base by virtual path (<c>WindowsPaths:/workspace</c> =
    /// <c>C:\proj</c>), what a launcher reads from <c>wslpath -w</c>. A Windows program run under WSL receives
    /// its path arguments in that spelling, and what it prints in it comes back virtual. A mount not named
    /// here whose base lies on a Windows drive (<c>/mnt/c/…</c>) gets the spelling of that drive.
    /// </summary>
    public Dictionary<string, string> WindowsPaths { get; } = new(StringComparer.Ordinal);
}
