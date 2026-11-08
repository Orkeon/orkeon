using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Application.Runs;

/// <summary>Where a recorded run stands.</summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public enum RunStatus
{
    /// <summary><c>run.started</c> was recorded and <c>run.finished</c> was not (yet).</summary>
    Running,

    /// <summary><c>run.finished</c> reported success.</summary>
    Succeeded,

    /// <summary><c>run.finished</c> reported a failure.</summary>
    Failed,

    /// <summary>The run was started, never finished, and its process no longer exists on this host: it was killed.</summary>
    Orphaned,

    /// <summary><c>run.finished</c> reported <c>status: "waiting_for_input"</c>: the run stopped on a question and resumes with its answer (FWK-23).</summary>
    WaitingForInput,
}

/// <summary>The store behind the run history (<c>Orkeon:RunHistory:Store</c>).</summary>
[Experimental("ORKEXP007", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public enum RunHistoryStore
{
    /// <summary>One JSONL file per run and an index, under the <c>/runs</c> mount.</summary>
    Jsonl,

    /// <summary>One Sqlite database (WAL) under the <c>/runs</c> mount.</summary>
    Sqlite,
}
