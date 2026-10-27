namespace Orkeon.Cli.Commands.Scripting.Runtime;

#pragma warning disable IDE1006 // lowercase members: this CLR type is marshalled to JS as the result of script-host.runCrew()
/// <summary>
/// Result of a crew run launched through <see cref="ScriptHostFacade"/>, marshalled to JS.
/// Property names are intentionally lowercase to read naturally from a
/// <c>*.cmd.ts</c> handler (<c>result.ok</c>, <c>result.summary</c>), matching the
/// <c>CommandResponse</c> convention.
/// </summary>
public sealed class CrewRunOutput
{
    /// <summary>Whether the crew completed without throwing.</summary>
    public bool ok { get; init; }

    /// <summary>The crew's aggregated output / last message, when available.</summary>
    public string? summary { get; init; }

    /// <summary>The raw run result object (crew name, final output, per-task timings).</summary>
    public object? artifacts { get; init; }

    /// <summary>
    /// True when any turn of this run was written to the console in the foreground (#228), so the script need
    /// not print <see cref="summary"/> again. Background and labelled output report false: streaming off, the
    /// turns written as whole prefixed lines - a crew task's (<c>[task] </c>), a background run's (<c>[t2] </c>).
    /// </summary>
    public bool streamed { get; init; }

    /// <summary>The failure message when <see cref="ok"/> is <see langword="false"/>.</summary>
    public string? error { get; init; }

    internal static CrewRunOutput Failure(string message) => new() { ok = false, error = message };
}
#pragma warning restore IDE1006
