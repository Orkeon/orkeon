using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Constants.Protocol;
using Orkeon.Studio.Core.Events;
using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.UseCases;

/// <summary>
/// The <c>orkeon usecases</c> argv Studio sends, composed against the CLI's grammar (STUDIO-38);
/// the drift pin is <c>UseCaseClientTests</c>' golden argv.
/// </summary>
public static class UseCaseArgumentsBuilder
{
    /// <summary>The CLI verb.</summary>
    public const string Verb = "usecases";

    /// <summary><c>usecases list --events jsonl</c>: the whole catalogue, every language, one line.</summary>
    public static IReadOnlyList<string> BuildList() => [Verb, "list", "--events", "jsonl"];

    /// <summary>
    /// <c>usecases search --events jsonl</c> without a text: the session mode — one query per stdin
    /// line, one answer per query, the end of stdin the end of the process.
    /// </summary>
    public static IReadOnlyList<string> BuildSession() => [Verb, "search", "--events", "jsonl"];

    /// <summary>
    /// <c>usecases export &lt;id&gt; --to &lt;folder&gt; --lang &lt;code&gt; --events jsonl</c> (STUDIO-41):
    /// the use case written as a team folder, named and described in <paramref name="language"/> —
    /// Studio's spelling (<c>zh</c>) read as the catalogue's (<c>zh-Hans</c>).
    /// </summary>
    public static IReadOnlyList<string> BuildExport(string id, string destination, string? language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        return [Verb, "export", id, "--to", destination, "--lang", UseCaseLanguages.FromUiLanguage(language), "--events", "jsonl"];
    }
}

/// <summary>What stood between Studio and an answer.</summary>
public enum UseCaseFailureKind
{
    /// <summary>No <c>orkeon</c> binary on this machine — the locator's words are the detail.</summary>
    EngineMissing,

    /// <summary>The CLI answered with an <c>error</c> line: its code and message are the detail.</summary>
    Refused,

    /// <summary>The process ended without the answer: its stderr, or its exit code, is the detail.</summary>
    Stopped,

    /// <summary>
    /// The team folder the CLI exported could not enter the teams root (STUDIO-41): the import's
    /// own refusal — the launcher's detector — or the disk's; the detail says which.
    /// </summary>
    NotImported,
}

/// <summary>Why a catalogue or a search did not come back, in the CLI's own words — never translated.</summary>
/// <param name="Kind">The family.</param>
/// <param name="Detail">The raw technical text.</param>
/// <param name="ExitCode">The child's exit code, when it ran and ended.</param>
/// <param name="Code">The CLI's error code, for a refusal.</param>
public sealed record UseCaseFailure(UseCaseFailureKind Kind, string Detail, int? ExitCode = null, string? Code = null);

/// <summary>A catalogue, or why there is none.</summary>
public sealed record UseCaseCatalogResult
{
    /// <summary>The catalogue; null when <see cref="Failure"/> says why.</summary>
    public UseCaseCatalog? Catalog { get; init; }

    /// <summary>Why no catalogue came back; null on success.</summary>
    public UseCaseFailure? Failure { get; init; }
}

/// <summary>An answer, or why there is none.</summary>
public sealed record UseCaseSearchResult
{
    /// <summary>The answer; null when <see cref="Failure"/> says why.</summary>
    public UseCaseAnswer? Answer { get; init; }

    /// <summary>Why no answer came back; null on success.</summary>
    public UseCaseFailure? Failure { get; init; }
}

/// <summary>A use case written as a team folder (STUDIO-41), or why it was not.</summary>
public sealed record UseCaseExportResult
{
    /// <summary>The folder the CLI wrote, as its <c>usecases.exported</c> line names it; null when <see cref="Failure"/> says why.</summary>
    public string? Folder { get; init; }

    /// <summary>Why no folder was written — a reference-only case, a folder that is not empty, no engine; null on success.</summary>
    public UseCaseFailure? Failure { get; init; }
}

/// <summary>
/// The Studio side of <c>orkeon usecases</c> (STUDIO-38/39/41): <c>list</c> run to completion for
/// the gallery, <c>export</c> run to completion for « Import as is », and ONE <c>search</c> session
/// kept open for the suggestions typed under the need —
/// started at the first query, answering every query after it, closed with the client. Modelled on
/// <c>ForgeClient</c>: the argv, the jsonl stream on stdout read through the same envelope parser,
/// JSON lines written to stdin, failures typed rather than thrown.
/// <para>
/// A session answers its queries by the envelope's <c>correlationId</c>, so two queries in flight
/// cannot be mixed up. A session that ends — the child crashed, or closed — fails what it had not
/// answered, and the next query opens a new one; a session that ended before ever announcing
/// itself (no binary, a CLI that knows no session mode) is not reopened by every keystroke: its
/// failure answers every later query, until a catalogue that loads shows the engine is back.
/// </para>
/// </summary>
public sealed class UseCaseClient : IDisposable
{
    /// <summary>The CLI's default answer length, sent explicitly so the wire says what Studio reads.</summary>
    public const int DefaultTop = 5;

    private static readonly JsonSerializerOptions QueryOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly OrkeonProcessRunner _runner;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private Session? _session;
    private UseCaseFailure? _unopenable;
    private int _queries;

    /// <summary>
    /// Creates a client over <paramref name="runner"/> — in the window, the runner the doctor and
    /// the launcher share, so the gallery can never read the catalogue of another binary than the
    /// one that runs the teams.
    /// </summary>
    public UseCaseClient(OrkeonProcessRunner runner) =>
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    /// <summary>Creates a client over an explicit launcher and locator (the test seam).</summary>
    public UseCaseClient(IProcessLauncher launcher, OrkeonBinaryLocator locator)
        : this(new OrkeonProcessRunner(launcher, locator))
    {
    }

    /// <summary>Creates a client over the real machine and real processes.</summary>
    public static UseCaseClient ForCurrentMachine() => new(OrkeonProcessRunner.ForCurrentMachine());

    /// <summary>Whether a search session is alive.</summary>
    public bool IsSessionOpen
    {
        get
        {
            lock (_gate)
                return _session is { Ended: false };
        }
    }

    /// <summary>
    /// Runs <c>usecases list --events jsonl</c> to completion and reads the catalogue off its one
    /// <c>usecases.catalog</c> line. Never throws for what the process did: no binary, an
    /// <c>error</c> line, an exit without a catalogue — each is a typed failure.
    /// </summary>
    public async Task<UseCaseCatalogResult> ListAsync(CancellationToken cancellationToken = default)
    {
        UseCaseCatalog? catalog = null;
        UseCaseFailure? refusal = null;
        var stderr = new List<string>();

        var run = await _runner.RunAsync(
            UseCaseArgumentsBuilder.BuildList(),
            onOutput: line =>
            {
                if (line.Channel == ProcessOutputChannel.StandardError)
                {
                    if (!string.IsNullOrWhiteSpace(line.Text))
                        stderr.Add(line.Text);
                }
                else if (OrkeonEventParser.TryParse(line.Text, out var orkeonEvent))
                {
                    if (UseCaseCatalog.TryRead(orkeonEvent!, out var read))
                        catalog = read;
                    else if (orkeonEvent!.Kind == UseCaseEventKinds.Error)
                        refusal = Refusal(orkeonEvent);
                }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (catalog is not null)
        {
            // A catalogue that loads is the engine answering: a session that could not open
            // before (no binary then) may be tried again.
            lock (_gate)
                _unopenable = null;
            return new UseCaseCatalogResult { Catalog = catalog };
        }

        if (refusal is not null)
            return new UseCaseCatalogResult { Failure = refusal };

        return new UseCaseCatalogResult
        {
            Failure = run.Outcome == RunOutcome.NotStarted
                ? new UseCaseFailure(UseCaseFailureKind.EngineMissing, run.Description)
                : Stopped(run, stderr, "no usecases.catalog line came back"),
        };
    }

    /// <summary>
    /// Runs <c>usecases export</c> to completion (STUDIO-41) and reads the folder off its one
    /// <c>usecases.exported</c> line. Never throws for what the process did: no binary, an
    /// <c>error</c> line — a reference-only case, a destination that is not empty — or an exit
    /// without the line, each is a typed failure.
    /// </summary>
    public async Task<UseCaseExportResult> ExportAsync(
        string id,
        string destination,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = UseCaseArgumentsBuilder.BuildExport(id, destination, language);
        string? folder = null;
        UseCaseFailure? refusal = null;
        var stderr = new List<string>();

        var run = await _runner.RunAsync(
            arguments,
            onOutput: line =>
            {
                if (line.Channel == ProcessOutputChannel.StandardError)
                {
                    if (!string.IsNullOrWhiteSpace(line.Text))
                        stderr.Add(line.Text);
                }
                else if (OrkeonEventParser.TryParse(line.Text, out var orkeonEvent))
                {
                    if (orkeonEvent!.Kind == UseCaseEventKinds.Exported)
                        folder = orkeonEvent.GetString("path");
                    else if (orkeonEvent.Kind == UseCaseEventKinds.Error)
                        refusal = Refusal(orkeonEvent);
                }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (folder is { Length: > 0 })
            return new UseCaseExportResult { Folder = folder };

        if (refusal is not null)
            return new UseCaseExportResult { Failure = refusal };

        return new UseCaseExportResult
        {
            Failure = run.Outcome == RunOutcome.NotStarted
                ? new UseCaseFailure(UseCaseFailureKind.EngineMissing, run.Description)
                : Stopped(run, stderr, "no usecases.exported line came back"),
        };
    }

    /// <summary>
    /// Asks the session for the use cases closest to <paramref name="text"/> — opening it on the
    /// first query. <paramref name="language"/> is sent as the query's <c>lang</c> when given (the
    /// CLI reads it from the text otherwise). Cancelling abandons the wait, not the query.
    /// </summary>
    public async Task<UseCaseSearchResult> SearchAsync(
        string text,
        int top = DefaultTop,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(top, 1);

        Session session;
        bool opened;
        lock (_gate)
        {
            if (_unopenable is { } unopenable)
                return new UseCaseSearchResult { Failure = unopenable };

            opened = _session is not { Ended: false };
            if (opened)
                _session = new Session();
            session = _session!;
        }

        if (opened && !Open(session))
            return new UseCaseSearchResult { Failure = session.EndFailure };

        var correlationId = string.Create(CultureInfo.InvariantCulture, $"q{Interlocked.Increment(ref _queries)}");
        var pending = new Pending(session);
        lock (_gate)
        {
            // The child may have died between the open and here, with nobody left to answer.
            if (session.Ended)
                return new UseCaseSearchResult { Failure = session.EndFailure };

            _pending[correlationId] = pending;
        }

        var line = JsonSerializer.Serialize(
            new QueryLine(UseCaseEventKinds.Query, correlationId, text.Trim(), top, language),
            QueryOptions);
        if (session.Input is not { } input || !input.TryWriteLine(line))
        {
            Complete(correlationId, new UseCaseSearchResult
            {
                Failure = new UseCaseFailure(UseCaseFailureKind.Stopped, "the search session no longer reads its input"),
            });
        }

        using var abandon = cancellationToken.Register(() =>
        {
            lock (_gate)
                _pending.Remove(correlationId);
            pending.Answer.TrySetCanceled(cancellationToken);
        });

        return await pending.Answer.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Closes the session's stdin — the CLI's own end: it finishes the query in hand and exits 0.
    /// Whatever it had not answered is failed now, not left waiting. Safe to call at any time.
    /// </summary>
    public void CloseSession()
    {
        Session? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
            if (session is null)
                return;

            // Deliberate, not a failure to open: the next query may open a new session.
            session.Closing = true;
        }

        session.Input?.Close();
        Fail(session, new UseCaseFailure(UseCaseFailureKind.Stopped, "the search session was closed"));
    }

    /// <inheritdoc />
    public void Dispose() => CloseSession();

    /// <summary>
    /// Starts <paramref name="session"/>'s child. False when it could not start or ended at once —
    /// its failure is then recorded, and remembered when the session never announced itself.
    /// </summary>
    private bool Open(Session session)
    {
        var run = _runner.RunAsync(
            UseCaseArgumentsBuilder.BuildSession(),
            onOutput: line => OnSessionLine(session, line),
            onInputReady: writer => session.Input = writer);

        // Continued rather than awaited: the session outlives this query. A child that already
        // ended — no binary, or a CLI without session mode, which exits at once — runs the
        // continuation inline.
        _ = run.ContinueWith(
            finished => End(session, EndFailure(session, finished)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        lock (_gate)
            return !session.Ended;
    }

    private void OnSessionLine(Session session, ProcessOutputLine line)
    {
        if (line.Channel == ProcessOutputChannel.StandardError)
        {
            if (!string.IsNullOrWhiteSpace(line.Text))
                session.Stderr.Add(line.Text);
            return;
        }

        if (!OrkeonEventParser.TryParse(line.Text, out var orkeonEvent))
            return;

        switch (orkeonEvent!.Kind)
        {
            case UseCaseEventKinds.Ready:
                session.Ready = true;
                break;
            case UseCaseEventKinds.Results when orkeonEvent.CorrelationId is { } answered
                                                 && UseCaseAnswer.TryRead(orkeonEvent, out var answer):
                Complete(answered, new UseCaseSearchResult { Answer = answer });
                break;
            case UseCaseEventKinds.Error when orkeonEvent.CorrelationId is { } refused:
                Complete(refused, new UseCaseSearchResult { Failure = Refusal(orkeonEvent) });
                break;
            case UseCaseEventKinds.Error:
                // An error naming no query is the session's own: the CLI is about to exit, and
                // this is the reason every query in flight will be given.
                session.Fault = Refusal(orkeonEvent);
                break;
        }
    }

    private void Complete(string correlationId, UseCaseSearchResult result)
    {
        Pending? pending;
        lock (_gate)
        {
            if (!_pending.Remove(correlationId, out pending))
                return;
        }

        pending.Answer.TrySetResult(result);
    }

    /// <summary>Marks <paramref name="session"/> ended and fails what it had not answered.</summary>
    private void End(Session session, UseCaseFailure failure)
    {
        lock (_gate)
        {
            if (session.Ended)
                return;

            session.Ended = true;
            session.EndFailure = failure;
            if (ReferenceEquals(_session, session))
                _session = null;

            // Never announced itself: opening it again would fail the same way, once per pause
            // in the typing. A session closed on purpose before it spoke proves nothing.
            if (!session.Ready && !session.Closing)
                _unopenable = failure;
        }

        Fail(session, failure);
    }

    private void Fail(Session session, UseCaseFailure failure)
    {
        List<Pending> orphans;
        lock (_gate)
        {
            var abandoned = _pending.Where(entry => ReferenceEquals(entry.Value.Session, session)).ToList();
            foreach (var entry in abandoned)
                _pending.Remove(entry.Key);
            orphans = [.. abandoned.Select(entry => entry.Value)];
        }

        foreach (var orphan in orphans)
            orphan.Answer.TrySetResult(new UseCaseSearchResult { Failure = failure });
    }

    private static UseCaseFailure EndFailure(Session session, Task<ProcessRunResult> finished)
    {
        if (session.Fault is { } fault)
            return fault;

        if (finished.IsFaulted)
        {
            return new UseCaseFailure(
                UseCaseFailureKind.Stopped,
                finished.Exception?.GetBaseException().Message ?? "the search session could not start");
        }

        if (finished.IsCanceled)
            return new UseCaseFailure(UseCaseFailureKind.Stopped, "the search session was stopped");

        var run = finished.Result;
        return run.Outcome == RunOutcome.NotStarted
            ? new UseCaseFailure(UseCaseFailureKind.EngineMissing, run.Description)
            : Stopped(run, session.Stderr, "the search session ended");
    }

    private static UseCaseFailure Refusal(OrkeonEvent errorEvent)
    {
        var code = errorEvent.GetString("code") ?? "";
        var message = errorEvent.GetString("message") ?? "";
        var detail = code.Length > 0 && message.Length > 0 ? $"{code}: {message}" : code + message;
        return new UseCaseFailure(UseCaseFailureKind.Refused, detail, Code: code.Length > 0 ? code : null);
    }

    private static UseCaseFailure Stopped(ProcessRunResult run, List<string> stderr, string what) =>
        new(
            UseCaseFailureKind.Stopped,
            stderr.Count > 0
                ? string.Join(Environment.NewLine, stderr)
                : string.Create(CultureInfo.InvariantCulture, $"{what} (exit {run.ExitCode})"),
            run.ExitCode);

    /// <summary>One inbound query, under the protocol's field names.</summary>
    private sealed record QueryLine(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("correlationId")] string CorrelationId,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("top")] int Top,
        [property: JsonPropertyName("lang")] string? Lang);

    /// <summary>One child of <c>usecases search</c> in session mode.</summary>
    private sealed class Session
    {
        public volatile IProcessInputWriter? Input;
        public volatile bool Ready;
        public volatile UseCaseFailure? Fault;
        public bool Closing;
        public bool Ended;
        public UseCaseFailure EndFailure = new(UseCaseFailureKind.Stopped, "the search session ended");
        public readonly List<string> Stderr = [];
    }

    /// <summary>One query waiting for its answer, and the session it went to.</summary>
    private sealed class Pending(Session session)
    {
        public Session Session { get; } = session;

        public TaskCompletionSource<UseCaseSearchResult> Answer { get; } = new();
    }
}
