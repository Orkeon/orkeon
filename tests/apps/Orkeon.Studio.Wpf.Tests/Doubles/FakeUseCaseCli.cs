using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>One sheet of the scripted catalogue: the fields the gallery reads, and the mounts an export records.</summary>
public sealed record FakeUseCase(
    string Id,
    string Category,
    string Process,
    IReadOnlyDictionary<string, string> Title,
    IReadOnlyDictionary<string, string> Problem,
    bool RequiresNetwork = false,
    IReadOnlyList<string>? RequiresKeys = null,
    bool Importable = true,
    IReadOnlyList<string>? Mounts = null);

/// <summary>One result of a scripted answer.</summary>
public sealed record FakeUseCaseResult(string Id, string Reason, params string[] Terms);

/// <summary>
/// <c>orkeon usecases</c>, scripted (STUDIO-39): <c>list</c> prints <see cref="Catalog"/> as one
/// <c>usecases.catalog</c> line; <c>search</c> in session mode announces itself, then stays open
/// and answers each query line through <see cref="Answer"/> — a conversation, which the ordinary
/// <see cref="FakeProcessLauncher"/> cannot hold. <c>export</c> (STUDIO-41) writes at <c>--to</c>
/// the team folder the CLI writes and answers with <c>usecases.exported</c> — or refuses, like the
/// CLI, a reference-only case, and any case while <see cref="ExportRefusal"/> is set. Nothing is
/// ever spawned.
/// </summary>
public sealed class FakeUseCaseCli : IProcessLauncher
{
    private static readonly JsonSerializerOptions SidecarOptions = new() { WriteIndented = true };

    private int _sequence;

    /// <summary>Every launch asked for.</summary>
    public List<ProcessLaunchRequest> Requests { get; } = [];

    /// <summary>The text of every query a session received, in order.</summary>
    public List<string> Queries { get; } = [];

    /// <summary>The <c>top</c> every query asked for, in order.</summary>
    public List<int> QueryTops { get; } = [];

    /// <summary>The sheets <c>list</c> answers with.</summary>
    public List<FakeUseCase> Catalog { get; } = [];

    /// <summary>The results of one query, by its text; none when null.</summary>
    public Func<string, IReadOnlyList<FakeUseCaseResult>>? Answer { get; set; }

    /// <summary>How many sessions had their stdin closed.</summary>
    public int ClosedSessions { get; private set; }

    /// <summary>How many <c>usecases list</c> ran.</summary>
    public int ListRuns => Requests.Count(request => request.Arguments is [_, "list", ..]);

    /// <summary>How many <c>usecases search</c> sessions started.</summary>
    public int SessionRuns => Requests.Count(request => request.Arguments is [_, "search", ..]);

    /// <summary>The <c>--to</c> of every <c>usecases export</c>, in order.</summary>
    public List<string> Exports { get; } = [];

    /// <summary>When set, every export is refused with this code and message, and writes nothing.</summary>
    public (string Code, string Message)? ExportRefusal { get; set; }

    /// <inheritdoc />
    public Task<ProcessRunResult> RunAsync(
        ProcessLaunchRequest request,
        Action<ProcessOutputLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Requests.Add(request);

        switch (request.Arguments)
        {
            case ["usecases", "list", ..]:
                onOutput?.Invoke(Out(CatalogLine()));
                return Task.FromResult(ProcessRunResult.FromExitCode(0, TimeSpan.Zero));

            case ["usecases", "search", ..]:
                return SessionAsync(request, onOutput);

            case ["usecases", "export", var id, "--to", var destination, "--lang", var language, ..]:
                return ExportAsync(id, destination, language, onOutput);

            default:
                return Task.FromResult(ProcessRunResult.FromExitCode(1, TimeSpan.Zero));
        }
    }

    /// <summary>
    /// The folder <c>usecases export</c> writes: the crew under <c>crew/</c>, sample data under
    /// <c>data/</c> when the case mounts it, a folder behind every mount, and the sidecar with the
    /// title and the problem in the language asked for.
    /// </summary>
    private async Task<ProcessRunResult> ExportAsync(
        string id, string destination, string language, Action<ProcessOutputLine>? onOutput)
    {
        Exports.Add(destination);
        var useCase = Catalog.Find(candidate => candidate.Id == id);
        var refusal = ExportRefusal
            ?? (useCase is null ? ("USECASES-UNKNOWN-ID", $"unknown use case '{id}'")
                : useCase.Importable ? null
                : ("USECASES-NOT-IMPORTABLE", $"'{id}' is reference only"));
        if (refusal is { } refused)
        {
            onOutput?.Invoke(Out(Line("error", null, new JsonObject
            {
                ["code"] = refused.Code,
                ["message"] = refused.Message,
                ["recoverable"] = false,
            })));
            return ProcessRunResult.FromExitCode(1, TimeSpan.Zero);
        }

        var mounts = useCase!.Mounts ?? [];
        Directory.CreateDirectory(Path.Combine(destination, "crew"));
        await File.WriteAllTextAsync(
            Path.Combine(destination, "crew", "config.yaml"),
            $"name: \"{id}\"\nprocess: \"{useCase.Process}\"\nagents:\n  worker:\n    role: \"Worker\"\ntasks:\n  work:\n    agent: \"worker\"\n").ConfigureAwait(false);
        foreach (var mount in mounts)
            Directory.CreateDirectory(Path.Combine(destination, mount[2..mount.IndexOf(':', StringComparison.Ordinal)]));
        if (mounts.Contains("./data:/data:ro"))
            await File.WriteAllTextAsync(Path.Combine(destination, "data", "sample.csv"), "region,sales\nnorth,12\n").ConfigureAwait(false);

        var name = TextIn(useCase.Title, language) ?? id;
        await File.WriteAllTextAsync(
            Path.Combine(destination, "studio-team.json"),
            JsonSerializer.Serialize(
                new JsonObject
                {
                    ["name"] = name,
                    ["description"] = TextIn(useCase.Problem, language),
                    ["mounts"] = new JsonArray([.. mounts.Select(mount => (JsonNode?)mount)]),
                },
                SidecarOptions)).ConfigureAwait(false);

        onOutput?.Invoke(Out(Line("usecases.exported", null, new JsonObject
        {
            ["id"] = id,
            ["path"] = destination,
            ["name"] = name,
            ["lang"] = language,
            ["files"] = new JsonArray("crew/config.yaml", "studio-team.json"),
            ["mounts"] = new JsonArray([.. mounts.Select(mount => (JsonNode?)mount)]),
        })));
        return ProcessRunResult.FromExitCode(0, TimeSpan.Zero);
    }

    /// <summary>The text in <paramref name="language"/>, else in English, else in French — the CLI's fall-back.</summary>
    private static string? TextIn(IReadOnlyDictionary<string, string> texts, string language) =>
        ((string[])[language, "en", "fr"])
            .Select(code => texts.TryGetValue(code, out var text) && text.Length > 0 ? text : null)
            .FirstOrDefault(text => text is not null);

    private async Task<ProcessRunResult> SessionAsync(ProcessLaunchRequest request, Action<ProcessOutputLine>? onOutput)
    {
        var closed = new TaskCompletionSource();
        request.OnInputReady?.Invoke(new SessionInput(this, onOutput, closed));
        onOutput?.Invoke(Out(Line("usecases.ready", null, new JsonObject { ["count"] = Catalog.Count })));

        await closed.Task.ConfigureAwait(false);
        return ProcessRunResult.FromExitCode(0, TimeSpan.Zero);
    }

    private string Reply(string queryLine)
    {
        var query = JsonNode.Parse(queryLine)!;
        var text = query["text"]!.GetValue<string>();
        Queries.Add(text);
        QueryTops.Add(query["top"]!.GetValue<int>());

        var results = new JsonArray();
        var rank = 0;
        foreach (var result in Answer?.Invoke(text) ?? [])
        {
            results.Add(new JsonObject
            {
                ["rank"] = ++rank,
                ["id"] = result.Id,
                ["score"] = 2.0 / rank,
                ["reason"] = result.Reason,
                ["terms"] = new JsonArray([.. result.Terms.Select(term => (JsonNode?)term)]),
            });
        }

        return Line("usecases.results", query["correlationId"]!.GetValue<string>(), new JsonObject
        {
            ["query"] = text,
            ["lang"] = "fr",
            ["langSource"] = "detected",
            ["mode"] = "bm25",
            ["results"] = results,
        });
    }

    private string CatalogLine()
    {
        var sheets = new JsonArray();
        foreach (var useCase in Catalog)
        {
            sheets.Add(new JsonObject
            {
                ["agents"] = 3,
                ["category"] = useCase.Category,
                ["format"] = "yaml",
                ["hasSampleData"] = false,
                ["id"] = useCase.Id,
                ["importable"] = useCase.Importable,
                ["mounts"] = new JsonArray([.. (useCase.Mounts ?? []).Select(mount => (JsonNode?)mount)]),
                ["number"] = sheets.Count + 1,
                ["problem"] = Texts(useCase.Problem),
                ["process"] = useCase.Process,
                ["requiresKeys"] = new JsonArray([.. (useCase.RequiresKeys ?? []).Select(key => (JsonNode?)key)]),
                ["requiresNetwork"] = useCase.RequiresNetwork,
                ["tags"] = new JsonArray(),
                ["tasks"] = 3,
                ["title"] = Texts(useCase.Title),
                ["tools"] = new JsonArray("file_read"),
            });
        }

        return Line("usecases.catalog", null, new JsonObject
        {
            ["count"] = Catalog.Count,
            ["languages"] = new JsonArray("fr", "en", "es", "de", "zh-Hans"),
            ["useCases"] = sheets,
        });
    }

    private static JsonObject Texts(IReadOnlyDictionary<string, string> texts)
    {
        var node = new JsonObject();
        foreach (var (language, text) in texts)
            node[language] = text;
        return node;
    }

    private string Line(string kind, string? correlationId, JsonObject payload)
    {
        var line = new JsonObject
        {
            ["v"] = 2,
            ["seq"] = ++_sequence,
            ["ts"] = "2026-09-24T10:00:00Z",
            ["kind"] = kind,
        };
        if (correlationId is not null)
            line["correlationId"] = correlationId;

        foreach (var property in payload.ToList())
        {
            payload.Remove(property.Key);
            line[property.Key] = property.Value;
        }

        return line.ToJsonString(new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private static ProcessOutputLine Out(string text) =>
        ProcessOutputLine.Now(ProcessOutputChannel.StandardOutput, text);

    /// <summary>A session's stdin: each line is answered on the spot, as a live child would.</summary>
    private sealed class SessionInput(FakeUseCaseCli owner, Action<ProcessOutputLine>? onOutput, TaskCompletionSource closed)
        : IProcessInputWriter
    {
        public bool TryWriteLine(string line)
        {
            if (closed.Task.IsCompleted)
                return false;

            onOutput?.Invoke(Out(owner.Reply(line)));
            return true;
        }

        public void Close()
        {
            if (closed.TrySetResult())
                owner.ClosedSessions++;
        }
    }
}
