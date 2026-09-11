using System.Collections.Concurrent;
using System.Diagnostics;

namespace Orkeon.Cli.TerminalGui.Telemetry;

/// <summary>
/// Aggregates tool calls into the reference UI's activity sentences —
/// <c>Read 1 file, ran 9 shell commands</c> — from the scripting runtime's own
/// <c>tool.call</c> spans (PLAN C3).
/// </summary>
/// <remarks>
/// The listener matches the <see cref="ActivitySource"/> by NAME ("Orkeon.Scripting"),
/// which is the whole reason this works from the UI layer: no project reference to
/// the scripting runtime is needed, spans only materialise once a listener attaches
/// (standard OpenTelemetry behaviour), and script-land changes nothing. Counting
/// happens on span-STOP so an aborted call is still an attempt that was made.
/// </remarks>
public sealed class ToolActivityAggregator : IDisposable
{
    private const string SourceName = "Orkeon.Scripting";
    // The gen_ai.* conventions the scripting runtime emits (Orkeon.Constants.Llm.GenAiAttributes;
    // spelled out here because this project references no Orkeon assembly): a tool span
    // is one whose gen_ai.operation.name is execute_tool, and its tool is gen_ai.tool.name.
    private const string OperationTag = "gen_ai.operation.name";
    private const string ExecuteToolOperation = "execute_tool";
    private const string ToolNameTag = "gen_ai.tool.name";

    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);
    private readonly ActivityListener _listener;
    private int _openToolSpans;

    public ToolActivityAggregator()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, SourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                if (IsToolSpan(activity))
                    Interlocked.Increment(ref _openToolSpans);
            },
            ActivityStopped = activity =>
            {
                if (!IsToolSpan(activity)) return;
                Interlocked.Decrement(ref _openToolSpans);
                var tool = activity.GetTagItem(ToolNameTag)?.ToString();
                if (!string.IsNullOrEmpty(tool))
                    _counts.AddOrUpdate(tool, 1, static (_, n) => n + 1);
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>True while at least one tool call is in flight (status line's `tool` state).</summary>
    public bool HasOpenToolCall => Volatile.Read(ref _openToolSpans) > 0;

    /// <summary>
    /// Drains the counts accumulated since the previous drain and renders the sentence,
    /// or "" when nothing ran. Called at turn boundaries so each sentence covers one turn.
    /// </summary>
    public string DrainSentence()
    {
        var snapshot = new List<KeyValuePair<string, int>>();
        foreach (var key in _counts.Keys)
        {
            if (_counts.TryRemove(key, out var n) && n > 0)
                snapshot.Add(new(key, n));
        }
        return ComposeSentence(snapshot);
    }

    /// <summary>
    /// Pure sentence composition, exposed for tests. Known tools get the reference's
    /// verb phrasing; anything else reads <c>used &lt;tool&gt; ×N</c> — a truthful generic
    /// beats a wrong verb. Order: reads, writes, shell, then the rest alphabetically.
    /// </summary>
    public static string ComposeSentence(IReadOnlyList<KeyValuePair<string, int>> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        if (counts.Count == 0) return string.Empty;

        var phrases = counts
            .OrderBy(kv => Rank(kv.Key))
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => Phrase(kv.Key, kv.Value))
            .ToList();

        // First phrase capitalised, the rest joined lowercase — "Read 1 file, ran 9 shell commands".
        phrases[0] = char.ToUpperInvariant(phrases[0][0]) + phrases[0][1..];
        return string.Join(", ", phrases);
    }

    private static int Rank(string tool) => tool switch
    {
        "file_read" => 0,
        "directory_read" or "directory_search" => 1,
        "file_write" => 2,
        "shell_command" => 3,
        _ => 4,
    };

    private static string Phrase(string tool, int n) => tool switch
    {
        "file_read" => $"read {n} file{Plural(n)}",
        "file_write" => $"wrote {n} file{Plural(n)}",
        "directory_read" => $"listed {n} director{(n == 1 ? "y" : "ies")}",
        "directory_search" => $"searched {n} director{(n == 1 ? "y" : "ies")}",
        "shell_command" => $"ran {n} shell command{Plural(n)}",
        "codebase_search" => $"searched the codebase ×{n}",
        _ => $"used {tool} ×{n}",
    };

    private static string Plural(int n) => n == 1 ? "" : "s";

    public void Dispose() => _listener.Dispose();

    private static bool IsToolSpan(Activity activity) =>
        string.Equals(activity.GetTagItem(OperationTag)?.ToString(), ExecuteToolOperation, StringComparison.Ordinal);
}
