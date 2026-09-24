using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using CommandLine;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Constants.Protocol;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Scripting.Cli.Commands.UseCases;

/// <summary>Options every <c>orkeon usecases</c> verb shares.</summary>
internal abstract class UseCasesCommandOptions
{
    /// <summary>The language of the titles shown — and, for a search, of the query.</summary>
    [Option("lang", Required = false,
        HelpText = "Language: fr, en, es, de or zh-Hans. For a search, the language of the query (read from the text when omitted); otherwise the language of the titles shown (default en).")]
    public string? Language { get; set; }

    /// <summary><c>--events jsonl</c>: the answer as protocol lines on stdout.</summary>
    [Option("events", Required = false,
        HelpText = "jsonl: answer with the versioned event protocol on stdout (the way Orkeon Studio reads it).")]
    public string? Events { get; set; }

    /// <summary>Test seam: a catalogue in place of the embedded one.</summary>
    internal UseCaseCatalog? Catalog { get; set; }
}

/// <summary>Parsed options of <c>orkeon usecases search</c>.</summary>
[Verb("search", HelpText = "Find the example use cases closest to a need in plain words — offline, without an LLM, in five languages.")]
internal sealed class UseCasesSearchOptions : UseCasesCommandOptions
{
    /// <summary>The need, word by word as the shell split it.</summary>
    [Value(0, Required = false, MetaName = "text",
        HelpText = "The need, in plain words. Omitted with --events jsonl: session mode, one JSON query per stdin line.")]
    public IEnumerable<string> Words { get; set; } = [];

    /// <summary>How many use cases to answer with.</summary>
    [Option("top", Required = false, Default = UseCaseSearchEngine.DefaultTop,
        HelpText = "Number of use cases to answer with (default 5).")]
    public int Top { get; set; } = UseCaseSearchEngine.DefaultTop;

    /// <summary>Test seam: a policy in place of <see cref="UseCaseSearchPolicy.Default"/>.</summary>
    internal UseCaseSearchPolicy? Policy { get; set; }

    /// <summary>Test seam: a model loader in place of <see cref="UseCaseLocalModel.Load"/>.</summary>
    internal Func<IEmbeddingProvider>? LoadModel { get; set; }
}

/// <summary>Parsed options of <c>orkeon usecases list</c>.</summary>
[Verb("list", HelpText = "List the example use cases, optionally filtered by category, process or tag.")]
internal sealed class UseCasesListOptions : UseCasesCommandOptions
{
    /// <summary>A category, by folder name (<c>03-finance-trading</c>) or name alone (<c>finance-trading</c>).</summary>
    [Option("category", Required = false, HelpText = "Only this category: 03-finance-trading, or finance-trading.")]
    public string? Category { get; set; }

    /// <summary>An orchestration mode.</summary>
    [Option("process", Required = false,
        HelpText = "Only this process: sequential, hierarchical, parallel, consensual, graph or autonomous.")]
    public string? Process { get; set; }

    /// <summary>A tag.</summary>
    [Option("tag", Required = false, HelpText = "Only the use cases carrying this tag.")]
    public string? Tag { get; set; }
}

/// <summary>Parsed options of <c>orkeon usecases show</c>.</summary>
[Verb("show", HelpText = "Show one use case: its sheet, the files the CLI carries for it, and optionally its crew.")]
internal sealed class UseCasesShowOptions : UseCasesCommandOptions
{
    /// <summary>The use case's id.</summary>
    [Value(0, Required = true, MetaName = "id", HelpText = "The use case's id, as `orkeon usecases list` prints it.")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Also print the crew file.</summary>
    [Option("crew", Required = false, Default = false, HelpText = "Also print the example's crew file.")]
    public bool Crew { get; set; }
}

/// <summary>
/// <c>orkeon usecases search | list | show</c> (STUDIO-38): the example catalogue, embedded in
/// the tool, searched without an LLM and without the network. One verb class per subcommand,
/// each parsing its own tail — <c>export</c> (STUDIO-41) is one more class and one more arm.
/// <para>
/// <c>search --events jsonl</c> without a text is the session mode Orkeon Studio keeps open while
/// its assistant is: one <see cref="UseCaseEventKinds.Query"/> per stdin line, one
/// <see cref="UseCaseEventKinds.Results"/> per query, the model loaded once for all of them, and
/// the end of stdin the end of the process.
/// </para>
/// </summary>
internal static class UseCasesCommand
{
    private const string EventsFormat = "jsonl";

    /// <summary>Parses <paramref name="args"/> (already stripped of <c>usecases</c>) and dispatches to a subcommand.</summary>
    public static async Task<int> DispatchAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        using var parser = new Parser(settings =>
        {
            settings.HelpWriter = Console.Out;
            settings.CaseInsensitiveEnumValues = true;
        });

        return await parser.ParseArguments<UseCasesSearchOptions, UseCasesListOptions, UseCasesShowOptions>(args)
            .MapResult(
                (UseCasesSearchOptions options) => ExecuteSearchAsync(options),
                (UseCasesListOptions options) => ExecuteListAsync(options),
                (UseCasesShowOptions options) => ExecuteShowAsync(options),
                _ => Task.FromResult(Program.ExitScriptError))
            .ConfigureAwait(false);
    }

    /// <summary>Runs one search, or the session mode; returns the CLI exit code.</summary>
    public static Task<int> ExecuteSearchAsync(UseCasesSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GuardedAsync("search", options, async (output, cancellationToken) =>
        {
            if (!TryReadLanguage(options, output, out var language))
                return Program.ExitScriptError;

            if (options.Top < 1)
            {
                return await output.FailAsync(UseCaseErrorCodes.OptionInvalid, $"--top must be at least 1 (got {options.Top}).")
                    .ConfigureAwait(false);
            }

            var text = string.Join(' ', options.Words).Trim();
            if (text.Length == 0 && output.Events is null)
            {
                return await output.FailAsync(UseCaseErrorCodes.OptionInvalid,
                    "a search text is required — or --events jsonl, to read one query per stdin line.").ConfigureAwait(false);
            }

            var catalog = options.Catalog ?? UseCaseCatalog.Embedded;
            var policy = options.Policy ?? UseCaseSearchPolicy.Default;
            using var engine = new UseCaseSearchEngine(catalog, policy, options.LoadModel ?? UseCaseLocalModel.Load);

            if (text.Length == 0)
            {
                return await RunSessionAsync(engine, catalog, policy, options.Top, language, output.Events!, cancellationToken)
                    .ConfigureAwait(false);
            }

            var answer = await engine
                .SearchAsync(new UseCaseQuery { Text = text, Top = options.Top, Language = language }, cancellationToken)
                .ConfigureAwait(false);

            if (output.Events is { } events)
                events.Results(answer, correlationId: null);
            else
                await Console.Out.WriteAsync(RenderAnswer(answer)).ConfigureAwait(false);

            return Program.ExitOk;
        });
    }

    /// <summary>Lists the catalogue, filtered; returns the CLI exit code.</summary>
    public static Task<int> ExecuteListAsync(UseCasesListOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GuardedAsync("list", options, async (output, _) =>
        {
            if (!TryReadLanguage(options, output, out var language))
                return Program.ExitScriptError;

            var catalog = options.Catalog ?? UseCaseCatalog.Embedded;
            IEnumerable<UseCase> selected = catalog.UseCases;

            if (!string.IsNullOrWhiteSpace(options.Category))
            {
                var categories = catalog.UseCases.Select(u => u.Category).Distinct(StringComparer.Ordinal).ToList();
                var category = categories.FirstOrDefault(c => NamesCategory(options.Category, c));
                if (category is null)
                {
                    return await output.FailAsync(UseCaseErrorCodes.OptionInvalid,
                        $"no category '{options.Category}'. Categories: {string.Join(", ", categories)}.").ConfigureAwait(false);
                }

                selected = selected.Where(u => u.Category == category);
            }

            if (!string.IsNullOrWhiteSpace(options.Process))
            {
                if (!ProcessType.TryFrom(options.Process.Trim(), out var process))
                {
                    return await output.FailAsync(UseCaseErrorCodes.OptionInvalid,
                        $"no process '{options.Process}'. Processes: {string.Join(", ", ProcessType.All)}.").ConfigureAwait(false);
                }

                selected = selected.Where(u => string.Equals(u.Process, process!.Value, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(options.Tag))
            {
                var tag = options.Tag.Trim();
                selected = selected.Where(u => u.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
            }

            var useCases = selected.ToList();
            if (output.Events is { } events)
                events.Catalog(catalog, useCases);
            else
                await Console.Out.WriteAsync(RenderList(useCases, language ?? UseCaseLanguages.Fallback)).ConfigureAwait(false);

            return Program.ExitOk;
        });
    }

    /// <summary>Shows one sheet; returns the CLI exit code.</summary>
    public static Task<int> ExecuteShowAsync(UseCasesShowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GuardedAsync("show", options, async (output, _) =>
        {
            if (!TryReadLanguage(options, output, out var language))
                return Program.ExitScriptError;

            var catalog = options.Catalog ?? UseCaseCatalog.Embedded;
            var id = options.Id.Trim();
            if (!catalog.TryGet(id, out var useCase))
            {
                var unknown = UnknownUseCaseException.For(id);
                return await output.FailAsync(UnknownUseCaseException.Code, unknown.Message, alreadyCoded: true).ConfigureAwait(false);
            }

            var files = catalog.FilesOf(useCase.Id);
            var crew = options.Crew ? catalog.ReadCrew(useCase.Id) : null;

            if (output.Events is { } events)
                events.Sheet(useCase, files, crew);
            else
                await Console.Out.WriteAsync(RenderSheet(useCase, files, crew, language)).ConfigureAwait(false);

            return Program.ExitOk;
        });
    }

    /// <summary>
    /// The session mode (D-07): announces itself, then answers each query line until stdin
    /// closes. A line that is not a query is skipped — the inbound stream is tolerant, as in
    /// every other verb; a query that cannot run is answered with an error naming it, and the
    /// session goes on. A query without <c>top</c> or <c>lang</c> takes the command line's
    /// <c>--top</c> (<paramref name="defaultTop"/>) and <c>--lang</c> (<paramref name="defaultLanguage"/>).
    /// </summary>
    [SuppressMessage("Design", "CA1031", Justification =
        "Session fault barrier: one query that fails is answered with a recoverable error event, and the next query is still served.")]
    private static async Task<int> RunSessionAsync(
        UseCaseSearchEngine engine,
        UseCaseCatalog catalog,
        UseCaseSearchPolicy policy,
        int defaultTop,
        string? defaultLanguage,
        UseCaseEventWriter events,
        CancellationToken cancellationToken)
    {
        events.Ready(catalog, policy);

        while (await ReadLineAsync(Console.In, cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!TryReadQuery(line, defaultTop, defaultLanguage, out var correlationId, out var query, out var problem))
            {
                if (problem is not null)
                    events.Error(UseCaseErrorCodes.QueryInvalid, problem, recoverable: true, correlationId);
                continue;
            }

            try
            {
                var answer = await engine.SearchAsync(query, cancellationToken).ConfigureAwait(false);
                events.Results(answer, correlationId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                events.Error(UseCaseErrorCodes.SearchFailed, ex.Message, recoverable: true, correlationId);
            }
        }

        return Program.ExitOk;
    }

    /// <summary>
    /// Stdin has no truly cancellable read — <c>ReadLineAsync(token)</c> checks the token, then
    /// blocks — so the wait, not the read, observes Ctrl+C.
    /// </summary>
    private static Task<string?> ReadLineAsync(TextReader input, CancellationToken cancellationToken) =>
        input.ReadLineAsync(cancellationToken).AsTask().WaitAsync(cancellationToken);

    /// <summary>
    /// Reads one inbound line. False with no problem: not a query, skip it. False with a
    /// problem: a query that cannot run, answered with an error.
    /// </summary>
    internal static bool TryReadQuery(
        string line,
        int defaultTop,
        string? defaultLanguage,
        out string? correlationId,
        [NotNullWhen(true)] out UseCaseQuery? query,
        out string? problem)
    {
        correlationId = null;
        query = null;
        problem = null;

        if (string.IsNullOrWhiteSpace(line))
            return false;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out var kind)
                || kind.ValueKind != JsonValueKind.String
                || kind.GetString() != UseCaseEventKinds.Query)
            {
                return false;
            }

            if (root.TryGetProperty("correlationId", out var id) && id.ValueKind == JsonValueKind.String)
                correlationId = id.GetString();

            if (!root.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(text.GetString()))
            {
                problem = "a query needs a non-empty \"text\".";
                return false;
            }

            var top = defaultTop;
            if (root.TryGetProperty("top", out var topValue)
                && (topValue.ValueKind != JsonValueKind.Number || !topValue.TryGetInt32(out top) || top < 1))
            {
                problem = "\"top\" must be a whole number of at least 1.";
                return false;
            }

            var language = defaultLanguage;
            if (root.TryGetProperty("lang", out var lang) && lang.ValueKind != JsonValueKind.Null
                && (lang.ValueKind != JsonValueKind.String || !UseCaseLanguages.TryParse(lang.GetString(), out language)))
            {
                problem = $"\"lang\" must be one of {string.Join(", ", UseCaseLanguages.All)}.";
                return false;
            }

            query = new UseCaseQuery { Text = text.GetString()!.Trim(), Top = top, Language = language };
            return true;
        }
    }

    private static bool TryReadLanguage(UseCasesCommandOptions options, Output output, out string? language)
    {
        language = null;
        if (options.Language is null || UseCaseLanguages.TryParse(options.Language, out language))
            return true;

        output.Fail(UseCaseErrorCodes.OptionInvalid,
            $"--lang must be one of {string.Join(", ", UseCaseLanguages.All)} (got '{options.Language}').");
        return false;
    }

    /// <summary>A category named by its folder (<c>03-finance-trading</c>) or by its name alone (<c>finance-trading</c>).</summary>
    private static bool NamesCategory(string given, string category)
    {
        var trimmed = given.Trim();
        if (string.Equals(trimmed, category, StringComparison.OrdinalIgnoreCase))
            return true;

        var dash = category.IndexOf('-', StringComparison.Ordinal);
        return dash > 0 && string.Equals(trimmed, category[(dash + 1)..], StringComparison.OrdinalIgnoreCase);
    }

    private static string RenderAnswer(UseCaseAnswer answer)
    {
        var text = new System.Text.StringBuilder();
        var how = answer.Mode == UseCaseSearchMode.Hybrid
            ? "searched by terms and meaning (BM25 + local embeddings, fused by RRF)"
            : answer.Degraded is null ? "searched by terms (BM25)" : "searched by terms only (BM25)";
        var language = $"{answer.Language}, {Spell(answer.LanguageSource)}";

        if (answer.Matches.Count == 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"No use case matches \"{answer.Query}\" ({language}; {how}). Try other words, or browse them all: orkeon usecases list");
        }
        else
        {
            var noun = answer.Matches.Count == 1 ? "use case" : "use cases";
            text.AppendLine(CultureInfo.InvariantCulture,
                $"{answer.Matches.Count} {noun} for \"{answer.Query}\" ({language}) — {how}:");

            foreach (var match in answer.Matches)
            {
                var title = match.UseCase.TitleIn(answer.Language);
                text.Append(CultureInfo.InvariantCulture, $"{match.Rank,2}. {match.UseCase.Id}");
                text.AppendLine(title.Length > 0 ? " — " + title : string.Empty);
                text.AppendLine(CultureInfo.InvariantCulture, $"    {Why(match)} · score {match.Score:0.####}");
            }
        }

        if (answer.Degraded is not null)
            text.AppendLine(CultureInfo.InvariantCulture, $"note: meaning is off for this search — {answer.Degraded}.");

        return text.ToString();
    }

    private static string Why(UseCaseMatch match)
    {
        var meaning = match.Similarity is { } similarity
            ? string.Create(CultureInfo.InvariantCulture, $"meaning {similarity:0.00}")
            : "meaning";

        return match.Reason switch
        {
            UseCaseMatchReason.Meaning => meaning,
            UseCaseMatchReason.TermsAndMeaning => $"terms: {string.Join(", ", match.Terms)} + {meaning}",
            _ => $"terms: {string.Join(", ", match.Terms)}",
        };
    }

    private static string RenderList(List<UseCase> useCases, string language)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"{useCases.Count} {(useCases.Count == 1 ? "use case" : "use cases")} ({language})");

        var idWidth = useCases.Count == 0 ? 0 : useCases.Max(u => u.Id.Length);
        foreach (var useCase in useCases)
        {
            var flags = Flags(useCase);
            var line = useCase.Id.PadRight(idWidth) + "  " + useCase.Process.PadRight(12) + useCase.TitleIn(language)
                + (flags.Count > 0 ? $"  [{string.Join(", ", flags)}]" : string.Empty);
            text.AppendLine(line.TrimEnd());
        }

        return text.ToString();
    }

    private static List<string> Flags(UseCase useCase)
    {
        var flags = new List<string>();
        if (useCase.HasSampleData)
            flags.Add("data");
        if (useCase.RequiresNetwork)
            flags.Add("web");
        if (useCase.RequiresKeys.Count > 0)
            flags.Add("keys");
        if (!useCase.Importable)
            flags.Add("reference only");
        return flags;
    }

    private static string RenderSheet(UseCase useCase, IReadOnlyList<UseCaseFile> files, string? crew, string? language)
    {
        var text = new System.Text.StringBuilder();
        var title = useCase.TitleIn(language ?? UseCaseLanguages.Fallback);
        text.Append(useCase.Id).AppendLine(title.Length > 0 ? " — " + title : string.Empty);

        Row(text, "category", useCase.Category);
        Row(text, "process", string.Create(CultureInfo.InvariantCulture,
            $"{useCase.Process} · {useCase.Agents} agents · {useCase.Tasks} tasks · {useCase.Format} crew ({useCase.CrewFileName})"));
        Row(text, "tools", useCase.Tools.Count > 0 ? string.Join(", ", useCase.Tools) : "none");
        Row(text, "tags", useCase.Tags.Count > 0 ? string.Join(", ", useCase.Tags) : "none");
        Row(text, "network", useCase.RequiresNetwork ? "needed" : "not needed");
        Row(text, "keys", useCase.RequiresKeys.Count > 0 ? string.Join(", ", useCase.RequiresKeys) : "none");
        Row(text, "mounts", useCase.Mounts.Count > 0 ? string.Join(", ", useCase.Mounts) : "none");
        Row(text, "import", useCase.Importable
            ? "importable"
            : "reference only — searchable and readable, not importable (its crew depends on files the CLI does not carry)");
        Row(text, "files", string.Join(", ", files.Select(f => string.Create(CultureInfo.InvariantCulture, $"{f.Path} ({f.Length} B)"))));

        foreach (var (label, texts) in (IEnumerable<(string, IReadOnlyDictionary<string, string>)>)[("title", useCase.Title), ("problem", useCase.Problem)])
        {
            var languages = language is null ? UseCaseLanguages.All : [language];
            var written = languages
                .Where(code => texts.TryGetValue(code, out var value) && !string.IsNullOrWhiteSpace(value))
                .ToList();
            if (written.Count == 0)
            {
                Row(text, label, "not written yet");
                continue;
            }

            for (var i = 0; i < written.Count; i++)
                Row(text, i == 0 ? label : string.Empty, $"{written[i]}: {texts[written[i]]}");
        }

        if (crew is not null)
        {
            text.AppendLine();
            text.AppendLine(CultureInfo.InvariantCulture, $"--- {useCase.CrewFileName} ---");
            text.Append(crew);
            if (!crew.EndsWith('\n'))
                text.AppendLine();
        }

        return text.ToString();
    }

    private static void Row(System.Text.StringBuilder text, string label, string value) =>
        text.Append("  ").Append(label.PadRight(10)).AppendLine(value);

    private static string Spell(UseCaseLanguageSource source) => source switch
    {
        UseCaseLanguageSource.Option => "--lang",
        UseCaseLanguageSource.Detected => "detected",
        _ => "default",
    };

    /// <summary>
    /// The verb's fault barrier and its two outputs: Ctrl+C → 130, a refused input → 1, anything
    /// unexpected → 2 — the exit codes every verb shares. With <c>--events jsonl</c> a refusal is
    /// an <c>error</c> line on stdout, the channel Studio reads; otherwise one line on stderr.
    /// </summary>
    [SuppressMessage("Design", "CA1031", Justification =
        "Top-level CLI fault barrier: an unexpected failure becomes the runtime-error exit code with one line, not a stack trace.")]
    private static async Task<int> GuardedAsync(
        string verb, UseCasesCommandOptions options, Func<Output, CancellationToken, Task<int>> body)
    {
        var events = options.Events;
        if (events is not null && !string.Equals(events, EventsFormat, StringComparison.OrdinalIgnoreCase))
        {
            await Console.Error.WriteLineAsync(
                $"orkeon usecases {verb}: unsupported --events format '{events}' — the only one is {EventsFormat} ({UseCaseErrorCodes.OptionInvalid}).")
                .ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        var output = new Output(verb, events is null ? null : new UseCaseEventWriter(Console.Out));

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            return await body(output, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Program.ExitCancelled;
        }
        catch (ArgumentException ex)
        {
            return await output.FailAsync(UseCaseErrorCodes.OptionInvalid, ex.Message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var message = $"unexpected error [{ex.GetType().FullName}]: {ex.Message}";
            if (output.Events is { } writer)
                writer.Error(UseCaseErrorCodes.Failed, message, recoverable: false);
            else
                await Console.Error.WriteLineAsync($"orkeon usecases {verb}: {message}").ConfigureAwait(false);
            return Program.ExitRuntimeError;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>Where a verb reports: the event writer in <c>--events</c> mode, stderr otherwise.</summary>
    private sealed record Output(string Verb, UseCaseEventWriter? Events)
    {
        /// <summary>Reports a refusal and returns exit code 1.</summary>
        public async Task<int> FailAsync(string code, string message, bool alreadyCoded = false)
        {
            if (Events is not null)
            {
                Events.Error(code, message, recoverable: false);
                return Program.ExitScriptError;
            }

            var line = alreadyCoded ? message : $"{message} ({code})";
            await Console.Error.WriteLineAsync($"orkeon usecases {Verb}: {line}").ConfigureAwait(false);
            return Program.ExitScriptError;
        }

        /// <summary>The synchronous twin of <see cref="FailAsync"/>, for the option checks that run before any await.</summary>
        public void Fail(string code, string message)
        {
            if (Events is not null)
                Events.Error(code, message, recoverable: false);
            else
                Console.Error.WriteLine($"orkeon usecases {Verb}: {message} ({code})");
        }
    }
}
