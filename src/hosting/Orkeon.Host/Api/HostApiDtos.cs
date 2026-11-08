using System.Text.Json.Serialization;
using Orkeon.Application.Runs;

namespace Orkeon.Host.Api;

/// <summary>The body of <c>POST /runs</c>.</summary>
internal sealed record StartRunRequest
{
    /// <summary>The hosted crew to run.</summary>
    [JsonPropertyName("crew")]
    public string? Crew { get; init; }

    /// <summary>What to run it on.</summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    /// <summary>The run's input variables, besides the prompt: strings, as <c>ICrewRunner</c> takes them.</summary>
    [JsonPropertyName("variables")]
    public Dictionary<string, string>? Variables { get; init; }

    /// <summary>The client's idempotency key: the same key with the same request starts one run.</summary>
    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; init; }
}

/// <summary>The answer of <c>POST /runs</c> and <c>POST /runs/{id}/cancel</c>.</summary>
internal sealed record RunAcceptedDto
{
    /// <summary>The run's id: the one the history, the checkpoints and A2A name it by.</summary>
    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    /// <summary>True when the idempotency key had been seen: this is the first request's run, not a new one.</summary>
    [JsonPropertyName("idempotent_replay")]
    public bool IdempotentReplay { get; init; }
}

/// <summary>One run, as <c>GET /runs</c> and <c>GET /runs/{id}</c> read it.</summary>
internal sealed record RunDto
{
    /// <summary>The run's id.</summary>
    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    /// <summary>The crew it runs.</summary>
    [JsonPropertyName("crew")]
    public string? Crew { get; init; }

    /// <summary><c>running</c>, <c>succeeded</c>, <c>failed</c> or <c>orphaned</c>.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>When it started.</summary>
    [JsonPropertyName("started_at")]
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>When it finished; absent while it runs.</summary>
    [JsonPropertyName("finished_at")]
    public DateTimeOffset? FinishedAt { get; init; }

    /// <summary>The message of the last error event of a failed run.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>The question the run holds on or stopped on; absent when it asks nothing (FWK-23).</summary>
    [JsonPropertyName("pending_input")]
    public PendingInputDto? PendingInput { get; init; }

    /// <summary>The DTO of a recorded run.</summary>
    public static RunDto From(RunSummary summary) => new()
    {
        RunId = summary.RunId,
        Crew = summary.CrewKey,
        Status = summary.Status switch
        {
            RunStatus.Running => "running",
            RunStatus.Succeeded => "succeeded",
            RunStatus.Failed => "failed",
            RunStatus.WaitingForInput => "waiting_for_input",
            _ => "orphaned",
        },
        StartedAt = summary.StartedAt,
        FinishedAt = summary.FinishedAt,
        Error = summary.Error,
    };
}

/// <summary>The question a run waits an answer for, as <c>GET /runs/{id}</c> reads it (FWK-23).</summary>
internal sealed record PendingInputDto
{
    /// <summary>The question's id: what <c>POST /runs/{id}/input</c> answers.</summary>
    [JsonPropertyName("request_id")]
    public required string RequestId { get; init; }

    /// <summary><c>text</c>, <c>confirm</c>, <c>choice</c>, <c>multi_choice</c> or <c>tool_approval</c>.</summary>
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    /// <summary>The prompt, as it was first asked.</summary>
    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    /// <summary>The options of a choice.</summary>
    [JsonPropertyName("choices")]
    public IReadOnlyList<string>? Choices { get; init; }
}

/// <summary>The body of <c>POST /runs/{id}/input</c>.</summary>
internal sealed record ProvideInputRequest
{
    /// <summary>The question answered; absent: the one the run waits on.</summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    /// <summary>The answer (a confirmation is yes or no, several choices a JSON array).</summary>
    [JsonPropertyName("answer")]
    public string? Answer { get; init; }
}

/// <summary>The answer of <c>POST /runs/{id}/input</c>.</summary>
internal sealed record InputAcceptedDto
{
    /// <summary>The run.</summary>
    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    /// <summary>The question answered.</summary>
    [JsonPropertyName("request_id")]
    public required string RequestId { get; init; }

    /// <summary><c>delivered</c> (the run was holding and goes on) or <c>recorded</c> (the run is suspended: resume it with <c>orkeon run --resume</c>).</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }
}

/// <summary>The answer of <c>GET /runs</c>.</summary>
internal sealed record RunListDto
{
    /// <summary>The runs, newest first.</summary>
    [JsonPropertyName("runs")]
    public required IReadOnlyList<RunDto> Runs { get; init; }
}

/// <summary>An error answer.</summary>
internal sealed record ApiErrorDto
{
    /// <summary>A stable code: <c>invalid_request</c>, <c>unknown_crew</c>, <c>at_limit</c>, <c>host_stopping</c>...</summary>
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    /// <summary>What to tell the caller; never a path, an endpoint or a credential.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

/// <summary>The answer of <c>/healthz</c> and <c>/readyz</c>.</summary>
internal sealed record ProbeDto
{
    /// <summary><c>healthy</c>, <c>degraded</c>, <c>unhealthy</c> or <c>stopping</c>.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>The checks the probe ran.</summary>
    [JsonPropertyName("checks")]
    public required IReadOnlyList<ProbeCheckDto> Checks { get; init; }
}

/// <summary>One check of a probe. The exception of a failed check stays in the log.</summary>
internal sealed record ProbeCheckDto
{
    /// <summary>The check's name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Its status.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>Its description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}
