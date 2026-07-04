using System.Collections.Immutable;

namespace Orkeon.Cli.Scripting.Loading;

/// <summary>
/// Summary returned by <see cref="ScriptCommandLoader.LoadAndRegisterAsync"/>. Lets the
/// host log a single-line breakdown without re-walking the directory.
/// </summary>
/// <param name="Loaded">Number of <see cref="CommandDescriptor"/> instances that survived validation.</param>
/// <param name="SkippedScripts">Number of script files skipped (read/transpile/evaluate failures).</param>
/// <param name="Conflicts">Number of descriptors rejected by global uniqueness validation.</param>
/// <param name="Errors">Human-readable error messages — one per skipped script / conflict.</param>
public sealed record CommandLoadResult(
    int Loaded,
    int SkippedScripts,
    int Conflicts,
    ImmutableArray<string> Errors)
{
    /// <summary>Convenience for callers that just want to know whether anything went sideways.</summary>
    public bool HasIssues => SkippedScripts > 0 || Conflicts > 0;
}
