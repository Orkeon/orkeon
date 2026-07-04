namespace Orkeon.Cli.Abstractions.Console;

/// <summary>
/// Supplies Tab-completion candidates to an interactive REPL input field:
/// command names (Tab on a leading <c>/token</c>) and virtual paths — files
/// <em>and</em> directories (Tab on an <c>@token</c>). Implementations are host-specific
/// (they know the command registries and the virtual file system); the input view stays generic.
/// </summary>
/// <remarks>
/// Both methods are synchronous and must be cheap/non-throwing: they run on the UI
/// thread when the user presses Tab. Implementations return an empty list rather than
/// throwing when nothing matches or a lookup is denied.
/// </remarks>
public interface IReplInputAssist
{
    /// <summary>
    /// Command names (and aliases) that start with <paramref name="prefix"/>, without the
    /// leading <c>/</c>. <paramref name="prefix"/> may be empty (return all). Case-insensitive.
    /// </summary>
    IReadOnlyList<string> CompleteCommand(string prefix);

    /// <summary>
    /// Virtual paths whose final segment starts with the leaf of <paramref name="prefix"/> — the
    /// text the user typed after <c>@</c> (e.g. <c>src/Pro</c>). Returns insertable values
    /// (to follow the <c>@</c>); directory candidates end with <c>/</c>. <paramref name="prefix"/>
    /// may be empty (return the workspace root's entries).
    /// </summary>
    IReadOnlyList<string> CompletePath(string prefix);
}
