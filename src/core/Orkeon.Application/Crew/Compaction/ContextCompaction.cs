using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Constants.Llm;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Crew.Compaction;

/// <summary>
/// What every loop of an orchestrator compacts with (#432): the settings, the window resolver, the
/// token counter, the observer, the pressure stages, and the provider of <c>Orkeon:Compaction:Profile</c>
/// when one is named.
/// </summary>
internal sealed record ContextCompactionServices
{
    internal required ContextCompactionOptions Options { get; init; }
    internal required IContextWindowResolver Windows { get; init; }
    internal required ILogger Logger { get; init; }
    internal ITokenCounter? TokenCounter { get; init; }
    internal IAgentCompactionObserver? Observer { get; init; }
    internal IReadOnlyList<IContextPressureStage> Stages { get; init; } = [];

    /// <summary>The provider of <c>Orkeon:Compaction:Profile</c>, resolved on first use; null for the agent's own model.</summary>
    internal Func<ILlmProvider?> SummaryProvider { get; init; } = static () => null;

    /// <summary>The compaction of a loop over <paramref name="profile"/>, whose provider's own configuration is <paramref name="baseConfig"/>.</summary>
    internal ContextCompaction For(string profile, LlmConfig? baseConfig) => new(this, profile, baseConfig);
}

/// <summary>The compaction of one loop (#432): one <see cref="ContextCompactor"/> per execution, or none when it is off.</summary>
internal sealed class ContextCompaction
{
    private readonly ContextCompactionServices _services;
    private readonly string _profile;
    private readonly LlmConfig? _baseConfig;

    internal ContextCompaction(ContextCompactionServices services, string profile, LlmConfig? baseConfig)
    {
        _services = services;
        _profile = profile;
        _baseConfig = baseConfig;
    }

    /// <summary>
    /// The compactor of one execution of <paramref name="agent"/> on <paramref name="taskId"/>, or null
    /// when compaction is off for it (globally, or by its <c>compaction: { enabled: false }</c>): the
    /// loop then trims as before.
    /// </summary>
    /// <param name="agent">The agent.</param>
    /// <param name="taskId">The task.</param>
    /// <param name="callConfig">The call's configuration: its model, cap and window; null when the loop has none.</param>
    /// <param name="maxOutputTokens">The call's output cap when the loop knows it apart from <paramref name="callConfig"/>.</param>
    /// <param name="ownModel">The summary call on the agent's own model.</param>
    /// <param name="gate">The loop's call gate, which a summary on another profile waits on too.</param>
    internal ContextCompactor? Begin(
        DomainAgent agent, object taskId, LlmConfig? callConfig, int? maxOutputTokens, SummaryCall ownModel, LlmCallGate gate)
    {
        var options = _services.Options;
        if (!(agent.Compaction?.Enabled ?? options.Enabled))
            return null;

        var model = string.IsNullOrWhiteSpace(callConfig?.Model) ? _baseConfig?.Model : callConfig.Model;
        // The profile's window is declared for the profile's own model: an agent on another model reads that model's.
        var onProfileModel = string.Equals(model, _baseConfig?.Model, StringComparison.OrdinalIgnoreCase);
        var window = _services.Windows.Resolve(_profile, model,
            callConfig?.ContextWindowTokens ?? (onProfileModel ? _baseConfig?.ContextWindowTokens : null));
        var other = _services.SummaryProvider();
        var setup = new ContextCompactorSetup
        {
            Window = window,
            SummaryInputTokens = other is null ? null : SummaryInputTokens(other.BaseConfig),
            ReservedOutputTokens = ReservedOutput(maxOutputTokens ?? callConfig?.MaxTokens ?? _baseConfig?.MaxTokens, model, window.Tokens),
            TriggerShare = agent.Compaction?.TriggerShare ?? options.TriggerShare,
            KeepRecentTurns = options.KeepRecentTurns,
            TaskId = taskId.ToString() ?? string.Empty,
            AgentRole = agent.Role.ToString(),
            AgentCompaction = RunCompaction(agent.Compaction),
            TokenCounter = _services.TokenCounter,
            Stages = _services.Stages,
            Observer = _services.Observer,
            Logger = _services.Logger,
        };
        var summaryCall = other is not null
            ? OverProvider(other, LlmConfig.OnProfile(options.Profile), gate, agent)
            : ownModel;
        return new ContextCompactor(setup, summaryCall);
    }

    /// <summary>
    /// The agent's compaction settings in the run in progress. A crew with <c>decision.mode: none</c> asks System One
    /// nothing (#429): in its run, an agent that leaves <c>prune</c> unset is out of the host's pruning of read-tool
    /// results, which the decision model judges. Read per run, never written onto the agent: an agent another crew
    /// shares keeps the host's setting there.
    /// </summary>
    internal static AgentCompaction? RunCompaction(AgentCompaction? own)
    {
        if (SystemOneRunScope.Current?.Mode != CrewDecisionMode.None)
            return own;
        if (own is null)
            return new AgentCompaction { Prune = false };
        return own.Prune is null ? own with { Prune = false } : own;
    }

    /// <summary>
    /// The prompt a summary call on <c>Orkeon:Compaction:Profile</c> can take: that profile's window (its own
    /// <c>ContextWindowTokens</c>, else its model's documented one) less its output reserve; null when neither is known,
    /// and the transcript is then sent unbounded.
    /// </summary>
    internal static int? SummaryInputTokens(LlmConfig? summaryConfig)
    {
        var window = summaryConfig?.ContextWindowTokens is > 0
            ? summaryConfig.ContextWindowTokens
            : LlmModelContextWindows.ContextWindowTokens(summaryConfig?.Model);
        return window is { } tokens ? tokens - ReservedOutput(summaryConfig?.MaxTokens, summaryConfig?.Model, tokens) : null;
    }

    /// <summary>
    /// The tokens held back for the call's output: its cap, else the model's documented maximum, else
    /// the 4096 fallback; at most half the window, so the usable window stays positive.
    /// </summary>
    internal static int ReservedOutput(int? maxTokens, string? model, int window)
    {
        int cap;
        if (maxTokens is > 0)
            cap = maxTokens.Value;
        else if (LlmModelOutputLimits.MaxOutputTokens(model) is { } documented && documented > 0)
            cap = documented;
        else
            cap = LlmDefaults.FallbackMaxOutputTokens;
        return Math.Min(cap, window / 2);
    }

    /// <summary>A summary call over <paramref name="provider"/>, after a turn in <paramref name="gate"/>, with no tool.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A summary call that fails is a compaction failure the breaker counts, never the task's: the caller's cancellation still propagates.")]
    internal static SummaryCall OverProvider(ILlmProvider provider, LlmConfig config, LlmCallGate gate, DomainAgent agent) =>
        async (instructions, transcript, cancellationToken) =>
        {
            try
            {
                await gate.WaitTurnAsync(agent, cancellationToken).ConfigureAwait(false);
                var response = await provider.ChatAsync(
                    [LlmMessage.System(instructions), LlmMessage.User(transcript)],
                    config with { Tools = null, ResponseFormat = null },
                    cancellationToken).ConfigureAwait(false);
                return new CompactionSummary(response.Content, response.TokensUsed, response.Error,
                    response.PromptTokens ?? 0, response.CompletionTokens ?? 0);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new CompactionSummary(null, 0, ex.Message);
            }
        };
}
