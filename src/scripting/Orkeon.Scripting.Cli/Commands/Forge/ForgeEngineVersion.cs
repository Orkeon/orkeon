using System.Reflection;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// The engine's own version — what build of <c>orkeon</c> is actually running this session.
/// <para>
/// It rides on <c>session.started</c> because a client cannot otherwise tell an engine that
/// reports nothing from an engine too old to report it. Studio launches whichever
/// <c>orkeon</c> its locator finds first — the co-installed one, the one on <c>PATH</c>, the
/// development tree's — and until this field existed, a session driven by a stale binary was
/// indistinguishable on screen from a working one that simply had nothing to say. That
/// ambiguity cost several round trips over a token meter that was correct all along.
/// </para>
/// <para>
/// The build metadata (<c>+sha</c>) is trimmed: the version is shown to a person, and the
/// commit belongs in <c>orkeon doctor</c>, not on every session line.
/// </para>
/// </summary>
internal static class ForgeEngineVersion
{
    /// <summary>The running assembly's informational version, without build metadata.</summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        var version = typeof(ForgeEngineVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        var metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata > 0 ? version[..metadata] : version;
    }
}
