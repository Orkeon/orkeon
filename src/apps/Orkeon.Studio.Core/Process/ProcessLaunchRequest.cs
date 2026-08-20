namespace Orkeon.Studio.Core.Process;

/// <summary>
/// What to spawn. Arguments are a list, never a command line: the child receives them
/// verbatim through <c>ProcessStartInfo.ArgumentList</c>, so a path holding spaces or
/// quotes needs no escaping and cannot be re-split.
/// </summary>
public sealed record ProcessLaunchRequest
{
    /// <summary>Default time a gracefully-signalled process is given before it is killed.</summary>
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(5);

    /// <summary>Absolute path of the executable to run.</summary>
    public required string FileName { get; init; }

    /// <summary>Arguments, one element per argv slot.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Working directory of the child; the parent's when null.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Environment variables added to (or overriding) the inherited environment.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// How long a cancelled process may keep running after the graceful stop request
    /// before it is killed with its whole tree.
    /// </summary>
    public TimeSpan GracePeriod { get; init; } = DefaultGracePeriod;

    /// <summary>
    /// When set, the child's standard input is redirected (UTF-8, no BOM) and this receives
    /// the writer right after the child starts, on the launching thread. Null — the default —
    /// leaves stdin alone, which is what every plain launch wants; a caller sets this only to
    /// drive a line-oriented dialogue with the child, and accepts that a child which reads
    /// stdin will wait on it.
    /// </summary>
    public Action<IProcessInputWriter>? OnInputReady { get; init; }
}
