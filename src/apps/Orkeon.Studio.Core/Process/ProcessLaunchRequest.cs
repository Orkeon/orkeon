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
}
