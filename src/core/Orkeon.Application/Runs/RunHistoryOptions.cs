using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Application.Runs;

/// <summary>The <c>Orkeon:RunHistory</c> section: whether the events of a run are kept after it ended, and where.</summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed class RunHistoryOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Orkeon:RunHistory";

    /// <summary>The virtual folder the history is kept in when <see cref="Path"/> is not set.</summary>
    public const string DefaultPath = "/runs";

    /// <summary>Whether the history is recorded. Off in the library, on in <c>orkeon-host</c> and the <c>orkeon init</c> template.</summary>
    public bool Enabled { get; set; }

    /// <summary>The store: <c>jsonl</c> (default) or <c>sqlite</c>.</summary>
    public RunHistoryStore Store { get; set; } = RunHistoryStore.Jsonl;

    /// <summary>A virtual path (it starts with '/'), under the <c>/runs</c> mount; the Sqlite file is <c>runs.db</c> inside it.</summary>
    public string Path { get; set; } = DefaultPath;

    /// <summary>Runs older than this many days are purged, at start and when a run finishes; 0 keeps them for ever.</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>At most this many runs are kept, the oldest purged first; 0 keeps them all.</summary>
    public int MaxRuns { get; set; } = 1000;

    /// <summary>Whether <c>llm.delta</c> events (token by token, verbose) are kept in the history. They always stay on the stdout stream.</summary>
    public bool IncludeDeltas { get; set; }
}
