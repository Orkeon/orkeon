namespace Orkeon.Constants.Protocol;

/// <summary>
/// The event kinds <c>orkeon usecases</c> exchanges with whatever drives it (STUDIO-38).
/// <para>
/// Same protocol as the run stream — one JSON document per line, the envelope of
/// <see cref="RunEventKinds"/> — and the same reason to declare the vocabulary once: the CLI
/// answers, Orkeon Studio asks, and the two cannot reference each other. Studio keeps one
/// <c>usecases search --events jsonl</c> process open while its assistant is open, sends a
/// <see cref="Query"/> down stdin after each pause in the typing, and matches every
/// <see cref="Results"/> to its query by the envelope's <c>correlationId</c>. A kind one side
/// spells differently is not an error anywhere — the reader skips it — so the drift would show
/// up as suggestions that never arrive. <see cref="All"/> is the set a consumer asserts against.
/// </para>
/// </summary>
public static class UseCaseEventKinds
{
    /// <summary>
    /// Inbound, session mode only: one search — <c>text</c>, and optionally <c>top</c> and
    /// <c>lang</c>. Answered by one <see cref="Results"/>, or by one <see cref="Error"/> when
    /// the query cannot be run, both carrying the query's <c>correlationId</c>.
    /// </summary>
    public const string Query = "usecases.query";

    /// <summary>
    /// Session mode is open: the catalogue is loaded and queries are read from stdin until it
    /// closes. Carries the catalogue size and the search mode of each language.
    /// </summary>
    public const string Ready = "usecases.ready";

    /// <summary>
    /// The ranked answer to one search: for each use case its id, score and why it matched
    /// (terms, meaning, or both), plus the mode the search ran in — and, when the language asked
    /// for meaning and the local model could not provide it, why not.
    /// </summary>
    public const string Results = "usecases.results";

    /// <summary>The catalogue, filtered by the <c>list</c> options: every sheet in full.</summary>
    public const string Catalog = "usecases.catalog";

    /// <summary>One use case in full, with the files the CLI embeds for it: the <c>show</c> answer.</summary>
    public const string Sheet = "usecases.sheet";

    /// <summary>
    /// One use case written as a team folder (STUDIO-41): the <c>export</c> answer — the folder,
    /// the team's name, the files written and the mounts its sidecar records. What Studio
    /// imports next.
    /// </summary>
    public const string Exported = "usecases.exported";

    /// <summary>An anomaly — the run stream's kind, with its shape: <c>code</c>, <c>message</c>, <c>recoverable</c>.</summary>
    public const string Error = RunEventKinds.Error;

    /// <summary>
    /// Every kind, so a consumer can assert it handles them all rather than discovering a gap
    /// as an answer that silently never arrives.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [Query, Ready, Results, Catalog, Sheet, Exported, Error];
}
