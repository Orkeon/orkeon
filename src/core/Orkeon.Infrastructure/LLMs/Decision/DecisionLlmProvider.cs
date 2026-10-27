using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Decision;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Infrastructure.LLMs.Decision;

/// <summary>
/// A decision profile: an <see cref="ILlmProvider"/> that runs its <see cref="IDecisionStage"/>s
/// around the calls it forwards to a target profile. Before the call, the request stages run in
/// order: a Block answers a structured error without calling any model, a Route sends the call to
/// another profile. After it, the response stages run in order: a Block replaces the answer with a
/// structured error, an Escalate sends the conversation again to a stronger profile — once; the
/// response stages then judge the new answer, and a second Escalate is ignored. The text is never
/// rewritten.
/// </summary>
/// <remarks>
/// <para>
/// A stage that fails — the decision model unreachable, a 429 after the retries — lets the call go
/// on with a warning (fail-open), or blocks it when the profile is fail-closed.
/// </para>
/// <para>
/// A stream is forwarded untouched when no stage judges the answer; otherwise the answer is
/// buffered, judged, then yielded whole — a stage cannot judge what the caller has already shown.
/// </para>
/// </remarks>
public sealed partial class DecisionLlmProvider : ILlmProvider, IStreamingLlmProvider
{
    /// <summary>The <see cref="LlmResponse.ErrorType"/> of a call a stage blocked.</summary>
    public const string BlockedErrorType = "DecisionBlocked";

    /// <summary>The metadata key naming the stage that blocked a call.</summary>
    public const string StageMetadataKey = "decision_stage";

    private readonly string _profile;
    private readonly ILlmProvider _target;
    private readonly IReadOnlyList<IDecisionStage> _requestStages;
    private readonly IReadOnlyList<IDecisionStage> _responseStages;
    private readonly Func<string, ILlmProvider> _resolve;
    private readonly bool _failClosed;
    private readonly ILogger _logger;
    private readonly IAuditLogger? _audit;

    /// <summary>Initializes a new instance of <see cref="DecisionLlmProvider"/>.</summary>
    /// <param name="profile">The decision profile's own name, in logs and audit events.</param>
    /// <param name="target">The provider a call goes to when no stage routes it.</param>
    /// <param name="requestStages">The stages that judge the conversation before the call, in order.</param>
    /// <param name="responseStages">The stages that judge the answer, in order.</param>
    /// <param name="resolve">Resolves the provider of a profile a stage routes or escalates to.</param>
    /// <param name="failClosed">Whether a failing stage blocks the call rather than letting it go on.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="audit">Receives each verdict other than Continue; null audits nothing.</param>
    public DecisionLlmProvider(
        string profile,
        ILlmProvider target,
        IReadOnlyList<IDecisionStage> requestStages,
        IReadOnlyList<IDecisionStage> responseStages,
        Func<string, ILlmProvider> resolve,
        bool failClosed,
        ILogger logger,
        IAuditLogger? audit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(requestStages);
        ArgumentNullException.ThrowIfNull(responseStages);
        ArgumentNullException.ThrowIfNull(resolve);
        ArgumentNullException.ThrowIfNull(logger);
        _profile = profile;
        _target = target;
        _requestStages = requestStages;
        _responseStages = responseStages;
        _resolve = resolve;
        _failClosed = failClosed;
        _logger = logger;
        _audit = audit;
    }

    /// <inheritdoc />
    public string Name => _target.Name;

    /// <inheritdoc />
    public LlmConfig? BaseConfig => _target.BaseConfig;

    /// <inheritdoc />
    public LlmProviderCapabilities Capabilities => _target.Capabilities;

    /// <inheritdoc />
    public bool SupportsStreaming => (_target as IStreamingLlmProvider)?.SupportsStreaming ?? false;

    /// <inheritdoc />
    public bool StreamsNatively => _responseStages.Count == 0 && ((_target as IStreamingLlmProvider)?.StreamsNatively ?? false);

    /// <inheritdoc />
    public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default) =>
        RunAsync([LlmMessage.User(prompt)], (provider, ct) => provider.GenerateAsync(prompt, config, ct), cancellationToken);

    /// <inheritdoc />
    public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return RunAsync(messages, (provider, ct) => provider.ChatAsync(messages, config, ct), cancellationToken);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> GenerateStreamingAsync(
        string prompt,
        LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LlmMessage[] messages = [LlmMessage.User(prompt)];
        if (_responseStages.Count == 0)
        {
            var (provider, blocked) = await BeforeAsync(messages, cancellationToken).ConfigureAwait(false);
            if (blocked is not null)
                throw Refusal(blocked);
            if (provider is IStreamingLlmProvider { SupportsStreaming: true } streaming)
            {
                await foreach (var chunk in streaming.GenerateStreamingAsync(prompt, config, cancellationToken).ConfigureAwait(false))
                    yield return chunk;
                yield break;
            }

            var direct = await provider.GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(direct.Content))
                yield return direct.Content;
            yield break;
        }

        var response = await GenerateAsync(prompt, config, cancellationToken).ConfigureAwait(false);
        if (response.ErrorType == BlockedErrorType)
            throw Refusal(response);
        if (!string.IsNullOrEmpty(response.Content))
            yield return response.Content;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LlmStreamEvent> ChatStreamingAsync(
        LlmMessage[] messages,
        LlmConfig? config = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (_responseStages.Count == 0)
        {
            var (provider, blocked) = await BeforeAsync(messages, cancellationToken).ConfigureAwait(false);
            if (blocked is not null)
            {
                yield return LlmStreamEvent.Complete(blocked);
                yield break;
            }

            if (provider is IStreamingLlmProvider { SupportsStreaming: true } streaming)
            {
                await foreach (var ev in streaming.ChatStreamingAsync(messages, config, cancellationToken).ConfigureAwait(false))
                    yield return ev;
                yield break;
            }

            var direct = await provider.ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(direct.Content))
                yield return LlmStreamEvent.Content(direct.Content);
            yield return LlmStreamEvent.Complete(direct);
            yield break;
        }

        var response = await ChatAsync(messages, config, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(response.Content))
            yield return LlmStreamEvent.Content(response.Content);
        yield return LlmStreamEvent.Complete(response);
    }

    private async Task<LlmResponse> RunAsync(
        LlmMessage[] messages,
        Func<ILlmProvider, CancellationToken, Task<LlmResponse>> call,
        CancellationToken cancellationToken)
    {
        var (provider, blocked) = await BeforeAsync(messages, cancellationToken).ConfigureAwait(false);
        if (blocked is not null)
            return blocked;

        var response = await call(provider, cancellationToken).ConfigureAwait(false);
        var escalated = false;
        var index = 0;
        while (index < _responseStages.Count && response.Error is null)
        {
            var stage = _responseStages[index];
            var verdict = await JudgeAsync(stage, ct => stage.OnResponseAsync(messages, response, ct), cancellationToken).ConfigureAwait(false);
            switch (verdict.Kind)
            {
                case DecisionVerdictKind.Block:
                    return Blocked(stage, verdict);
                case DecisionVerdictKind.Escalate or DecisionVerdictKind.Route when escalated:
                    LogSecondEscalationIgnored(_profile, stage.Name, verdict.Profile);
                    break;
                case DecisionVerdictKind.Escalate or DecisionVerdictKind.Route:
                    escalated = true;
                    response = await call(_resolve(verdict.Profile!), cancellationToken).ConfigureAwait(false);
                    // The new answer is judged from the first response stage on.
                    index = 0;
                    continue;
            }

            index++;
        }

        return response;
    }

    /// <summary>
    /// Runs the request stages: the provider the call goes to, or the error answer of a block.
    /// A Route changes the provider and the next stages still run; an Escalate before the call is
    /// a Route.
    /// </summary>
    private async Task<(ILlmProvider Provider, LlmResponse? Blocked)> BeforeAsync(LlmMessage[] messages, CancellationToken cancellationToken)
    {
        var provider = _target;
        foreach (var stage in _requestStages)
        {
            var verdict = await JudgeAsync(stage, ct => stage.OnRequestAsync(messages, ct), cancellationToken).ConfigureAwait(false);
            switch (verdict.Kind)
            {
                case DecisionVerdictKind.Block:
                    return (provider, Blocked(stage, verdict));
                case DecisionVerdictKind.Route or DecisionVerdictKind.Escalate:
                    provider = _resolve(verdict.Profile!);
                    break;
            }
        }

        return (provider, null);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031",
        Justification = "Fail-open: a stage that fails must not fail the call it judges; fail-closed turns the failure into a block.")]
    private async Task<DecisionVerdict> JudgeAsync(
        IDecisionStage stage,
        Func<CancellationToken, Task<DecisionVerdict>> judge,
        CancellationToken cancellationToken)
    {
        DecisionVerdict verdict;
        try
        {
            verdict = await judge(cancellationToken).ConfigureAwait(false) ?? DecisionVerdict.Continue;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (_failClosed)
            {
                LogStageFailedClosed(ex, _profile, stage.Name);
                verdict = DecisionVerdict.Block($"the decision stage failed and the profile is fail-closed: {ex.Message}");
            }
            else
            {
                LogStageFailedOpen(ex, _profile, stage.Name);
                return DecisionVerdict.Continue;
            }
        }

        if (verdict.Kind != DecisionVerdictKind.Continue)
        {
            LogVerdict(_profile, stage.Name, verdict.Kind, verdict.Profile, verdict.Reason);
            await AuditAsync(stage, verdict, cancellationToken).ConfigureAwait(false);
        }

        return verdict;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031",
        Justification = "Audit fault barrier: a failing audit sink must never fail the call it records.")]
    private async Task AuditAsync(IDecisionStage stage, DecisionVerdict verdict, CancellationToken cancellationToken)
    {
        if (_audit is null)
            return;
        var attribution = LlmUsageScope.Current;
        var details = new Dictionary<string, string>
        {
            ["profile"] = _profile,
            ["stage"] = stage.Name,
            ["verdict"] = verdict.Kind.ToString(),
        };
        if (verdict.Profile is not null)
            details["to"] = verdict.Profile;
        try
        {
            await _audit.LogAsync(new AuditEvent
            {
                EventId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                Category = AuditCategory.AgentDecision,
                Action = AuditAction(verdict.Kind),
                Outcome = verdict.Kind == DecisionVerdictKind.Block ? AuditOutcome.Blocked : AuditOutcome.Success,
                CorrelationId = string.IsNullOrEmpty(attribution.TaskId) ? _profile : attribution.TaskId,
                CrewId = string.IsNullOrEmpty(attribution.CrewId) ? null : attribution.CrewId,
                TaskId = string.IsNullOrEmpty(attribution.TaskId) ? null : attribution.TaskId,
                Details = details,
                Severity = verdict.Kind == DecisionVerdictKind.Block ? AuditSeverity.Warning : AuditSeverity.Info,
                Message = verdict.Reason,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAuditFailed(ex, _profile);
        }
    }

    private static string AuditAction(DecisionVerdictKind kind) => kind switch
    {
        DecisionVerdictKind.Block => "decision.block",
        DecisionVerdictKind.Route => "decision.route",
        DecisionVerdictKind.Escalate => "decision.escalate",
        _ => "decision.continue",
    };

    private LlmResponse Blocked(IDecisionStage stage, DecisionVerdict verdict) => new()
    {
        Content = string.Empty,
        Metadata = new Dictionary<string, object>
        {
            [LlmResponseMetadataKeys.Error] = $"Blocked by the decision stage '{stage.Name}' of profile '{_profile}': {verdict.Reason}",
            [LlmResponseMetadataKeys.ErrorType] = BlockedErrorType,
            [StageMetadataKey] = stage.Name,
        },
    };

    private static InvalidOperationException Refusal(LlmResponse blocked) => new(blocked.Error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Decision profile {Profile}: stage {Stage} decided {Verdict} {To} ({Reason})")]
    private partial void LogVerdict(string profile, string stage, DecisionVerdictKind verdict, string? to, string? reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Decision profile {Profile}: stage {Stage} failed; the call goes on (fail-open)")]
    private partial void LogStageFailedOpen(Exception exception, string profile, string stage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Decision profile {Profile}: stage {Stage} failed; the call is blocked (fail-closed)")]
    private partial void LogStageFailedClosed(Exception exception, string profile, string stage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Decision profile {Profile}: stage {Stage} asked a second escalation to {To}; ignored")]
    private partial void LogSecondEscalationIgnored(string profile, string stage, string? to);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Decision profile {Profile}: the audit sink failed")]
    private partial void LogAuditFailed(Exception exception, string profile);
}
