namespace Orkeon.Cli.Commands.Scripting.Telemetry;

/// <summary>
/// Constants used as <see cref="System.Diagnostics.Activity"/> tag keys for the
/// scripted-commands subsystem. Tags are attached to spans produced via
/// <c>ScriptingActivitySource</c> (cf. spec §10).
/// </summary>
public static class CliScriptingTags
{
    /// <summary><c>script:&lt;virtualPath&gt;</c> — distinguishes scripted vs native commands in traces.</summary>
    public const string CommandSource = "orkeon.cli.command.source";

    /// <summary>Logical command name as typed by the user.</summary>
    public const string CommandName = "orkeon.cli.command.name";
}
