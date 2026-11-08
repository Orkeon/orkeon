using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Checkpointing;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.HumanInput;
using Orkeon.Infrastructure.Checkpointing;

namespace Orkeon.Infrastructure.HumanInput;

/// <summary>
/// Decorator that makes a question to a human outlive the process (FWK-23): it derives the question's id from the task
/// that asks (<c>{TaskKey}#{n}.{q}</c>), persists the question with the run's checkpoint session, and then either holds
/// for the answer (<see cref="HumanInputPause.Hold"/>, up to the hold timeout) or suspends the run
/// (<see cref="HumanInputPause.Suspend"/>, <see cref="RunSuspendedException"/>). The answer is matched by the id alone and
/// the prompt shown is the one persisted the first time, so a resumed task that words its question differently still
/// gets its answer and never loops. Outside a task of a checkpointed run it is transparent.
/// </summary>
public sealed partial class DurableHumanInputProvider : IHumanInputProvider
{
    private readonly IHumanInputProvider _inner;
    private readonly IRunScope _runScope;
    private readonly IPendingInputStore _store;
    private readonly IPendingInputBroker _broker;
    private readonly IOptions<HumanInputPauseOptions> _options;
    private readonly TimeProvider _time;
    private readonly ILogger<DurableHumanInputProvider> _logger;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="inner">The provider that asks a person, when one can be asked.</param>
    /// <param name="runScope">The ambient run.</param>
    /// <param name="store">Where the question is kept.</param>
    /// <param name="broker">Where a held run meets its answer.</param>
    /// <param name="options">The pause policy.</param>
    /// <param name="time">The clock of the hold timeout.</param>
    /// <param name="logger">The logger.</param>
    public DurableHumanInputProvider(
        IHumanInputProvider inner,
        IRunScope runScope,
        IPendingInputStore store,
        IPendingInputBroker broker,
        IOptions<HumanInputPauseOptions> options,
        TimeProvider time,
        ILogger<DurableHumanInputProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(runScope);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(broker);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _inner = inner;
        _runScope = runScope;
        _store = store;
        _broker = broker;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool AsksAPerson => _inner.AsksAPerson;

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<string> GetInputAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return await AskAsync(context, PendingInputKind.Text, _inner.GetInputAsync, cancellationToken).ConfigureAwait(false)
            ?? context.DefaultValue
            ?? string.Empty;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<bool> GetConfirmationAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var answer = await AskAsync(
            context,
            PendingInputKind.Confirm,
            async (c, t) => await _inner.GetConfirmationAsync(c, t).ConfigureAwait(false) ? "yes" : "no",
            cancellationToken).ConfigureAwait(false);
        return answer is not null && IsAffirmative(answer);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<string> GetChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var answer = await AskAsync(context, PendingInputKind.Choice, _inner.GetChoiceAsync, cancellationToken).ConfigureAwait(false);
        if (answer is not null && (context.Options.Count == 0 || context.Options.Contains(answer, StringComparer.Ordinal)))
            return answer;

        return context.DefaultValue ?? (context.Options.Count > 0 ? context.Options[0] : string.Empty);
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<IReadOnlyList<string>> GetMultiChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var answer = await AskAsync(
            context,
            PendingInputKind.MultiChoice,
            async (c, t) => JsonSerializer.Serialize(await _inner.GetMultiChoiceAsync(c, t).ConfigureAwait(false)),
            cancellationToken).ConfigureAwait(false);
        return ParseMany(answer);
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        _inner.IsAvailableAsync(cancellationToken);

    /// <summary>The permission gate's question about one tool call, as a durable question of kind <see cref="PendingInputKind.ToolApproval"/>.</summary>
    internal System.Threading.Tasks.Task<string?> AskForApprovalAsync(
        string prompt,
        string toolName,
        string argsHash,
        IReadOnlyList<string> choices,
        Func<HumanInputContext, CancellationToken, System.Threading.Tasks.Task<string>> askThePerson,
        CancellationToken ct) =>
        AskAsync(
            new HumanInputContext { Prompt = prompt, InputType = "approval", Options = choices },
            PendingInputKind.ToolApproval,
            askThePerson,
            ct,
            request => request with { ToolName = toolName, ArgsHash = argsHash });

    private async System.Threading.Tasks.Task<string?> AskAsync(
        HumanInputContext context,
        PendingInputKind kind,
        Func<HumanInputContext, CancellationToken, System.Threading.Tasks.Task<string>> askThePerson,
        CancellationToken ct,
        Func<PendingInputRequest, PendingInputRequest>? shape = null)
    {
        if (_runScope.Current is not { } run || TaskInputScope.Current is not { } task)
            return await askThePerson(context, ct).ConfigureAwait(false);

        // Another task already stopped the run on its question: nothing more is asked.
        if (run.Suspension is { } already)
            throw new RunSuspendedException(already.RequestId);

        var sessionId = run.ExecutionId.ToString();
        var requestId = task.NextRequestId();
        var request = new PendingInputRequest
        {
            RequestId = requestId,
            Kind = kind,
            Prompt = context.Prompt,
            Choices = [.. context.Options],
            TaskKey = task.Key,
            CreatedAt = _time.GetUtcNow(),
        };
        if (shape is not null)
            request = shape(request);

        // Unset, the policy follows who is there: a run with a person to ask holds for the answer, one with nobody stops.
        var pause = _options.Value.Pause ?? (_inner.AsksAPerson ? HumanInputPause.Hold : HumanInputPause.Suspend);

        // The waiter exists before the question is visible to anyone: an answer cannot get ahead of it.
        using var timeout = pause == HumanInputPause.Hold && _options.Value.HoldTimeout is { } limit
            ? new CancellationTokenSource(limit, _time)
            : new CancellationTokenSource();
        using var hold = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var held = pause == HumanInputPause.Hold ? _broker.WaitAsync(sessionId, requestId, hold.Token) : null;
        var answer = await AwaitAsync().ConfigureAwait(false);
        return answer;

        async System.Threading.Tasks.Task<string?> AwaitAsync()
        {
            try
            {
                var asked = await _store.AskAsync(sessionId, request, ct).ConfigureAwait(false);
                if (asked.Answer is { } stored)
                {
                    await hold.CancelAsync().ConfigureAwait(false);
                    return stored;
                }

                var shown = asked.Pending ?? request;                if (pause == HumanInputPause.Suspend)
                {
                    if (asked.Pending is not null)
                        await NotifyAsync(run, new HumanInputNotice(HumanInputNoticeKind.Needed, shown, Suspending: true), ct).ConfigureAwait(false);
                    throw Suspend(run, shown);
                }

                return await HoldAsync(run, sessionId, requestId, shown, context, askThePerson, held!, hold, timeout, ct).ConfigureAwait(false);
            }
            finally
            {
                // Whatever ended the wait, no waiter outlives the call.
                await hold.CancelAsync().ConfigureAwait(false);
                if (held is not null)
                    await ObserveAsync(held).ConfigureAwait(false);
            }
        }
    }

    private async System.Threading.Tasks.Task<string?> HoldAsync(
        RunScope run,
        string sessionId,
        string requestId,
        PendingInputRequest shown,
        HumanInputContext context,
        Func<HumanInputContext, CancellationToken, System.Threading.Tasks.Task<string>> askThePerson,
        System.Threading.Tasks.Task<string> held,
        CancellationTokenSource hold,
        CancellationTokenSource timeout,
        CancellationToken ct)
    {
        // A provider that asks a person tells the stream itself (with the id in its context); any other leaves it to us.
        System.Threading.Tasks.Task<string>? person = null;
        var innerAsks = _inner.AsksAPerson;
        await NotifyAsync(run, new HumanInputNotice(HumanInputNoticeKind.Needed, shown, Suspending: false), ct, innerAsks).ConfigureAwait(false);
        if (innerAsks)
            person = askThePerson(With(context, shown, requestId), hold.Token);

        string answer;
        try
        {
            var first = person is null ? held : await System.Threading.Tasks.Task.WhenAny(held, person).ConfigureAwait(false);
            answer = await first.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            LogHoldExpired(requestId);
            throw Suspend(run, shown);
        }

        await _store.RecordAnswerAsync(sessionId, requestId, answer, CancellationToken.None).ConfigureAwait(false);
        await NotifyAsync(run, new HumanInputNotice(HumanInputNoticeKind.Given, shown), ct).ConfigureAwait(false);
        return answer;
    }

    private static RunSuspendedException Suspend(RunScope run, PendingInputRequest shown)
    {
        run.Suspend(shown);
        return new RunSuspendedException(run.Suspension!.RequestId);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "An observer of the run never changes its outcome.")]
    private async System.Threading.Tasks.Task NotifyAsync(RunScope run, HumanInputNotice notice, CancellationToken ct, bool skip = false)
    {
        if (skip || run.HumanInputObserver is not { } observer)
            return;

        try
        {
            await observer(notice, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogObserverFailed(ex, notice.Request.RequestId);
        }
    }

    private static async System.Threading.Tasks.Task ObserveAsync(System.Threading.Tasks.Task waiter)
    {
        try
        {
            await waiter.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The wait ended with the call.
        }
    }

    private static HumanInputContext With(HumanInputContext context, PendingInputRequest shown, string requestId) => new()
    {
        RequestId = context.RequestId,
        AgentId = context.AgentId,
        AgentRole = context.AgentRole,
        TaskId = context.TaskId,
        TaskDescription = context.TaskDescription,
        Prompt = shown.Prompt,
        InputType = context.InputType,
        Options = context.Options,
        Metadata = new Dictionary<string, object>(context.Metadata) { [HumanInputPauseMetadata.RequestId] = requestId },
        Timeout = context.Timeout,
        IsRequired = context.IsRequired,
        DefaultValue = context.DefaultValue,
        ValidationRules = context.ValidationRules,
        Recommended = context.Recommended,
        Multi = context.Multi,
        CreatedAt = context.CreatedAt,
    };

    private static bool IsAffirmative(string answer) =>
        answer.Trim().ToUpperInvariant() is "Y" or "YES" or "TRUE" or "1" or "O" or "OUI" or "APPROVED" or "ALLOW";

    private static string[] ParseMany(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return [];

        var trimmed = answer.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<string[]>(trimmed) ?? [];
            }
            catch (JsonException)
            {
                trimmed = trimmed.Trim('[', ']');
            }
        }

        return [.. trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(static item => item.Trim('\'', '"', ' '))
            .Where(static item => item.Length > 0)];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The hold on question {RequestId} expired: the run suspends, the question stays persisted")]
    private partial void LogHoldExpired(string requestId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An observer failed on the notice of question {RequestId}")]
    private partial void LogObserverFailed(Exception ex, string requestId);
}

/// <summary>The provider of a durable asker that has no person behind it: the question is persisted, never put to anyone.</summary>
internal sealed class NoPerson(bool asksAPerson) : IHumanInputProvider
{
    public bool AsksAPerson => asksAPerson;

    public System.Threading.Tasks.Task<string> GetInputAsync(HumanInputContext context, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public System.Threading.Tasks.Task<bool> GetConfirmationAsync(HumanInputContext context, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public System.Threading.Tasks.Task<string> GetChoiceAsync(HumanInputContext context, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public System.Threading.Tasks.Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        System.Threading.Tasks.Task.FromResult(false);
}

/// <summary>A stable digest of the arguments of a tool call: what a question about it names, without ever writing the values.</summary>
internal static class ArgumentsHash
{
    public static string Of(IReadOnlyDictionary<string, object?> arguments)
    {
        var text = string.Join('\u0001', arguments.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={JsonSerializer.Serialize(p.Value)}"));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..16];
    }
}
