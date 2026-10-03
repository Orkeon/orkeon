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
}
