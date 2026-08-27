using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Crew.Strategies;

/// <summary>
/// The orchestration side of <see cref="OrkeonApplicationOptions.AgentSelectionStrategy"/>:
/// when a task names no agent, who runs it.
/// <para>
/// The option, its three strategies, their DI wiring and their tests all existed — and
/// <see cref="IAgentSelectionService.SelectBestAgentAsync"/> had no caller anywhere in
/// <c>src/</c>. Setting <c>AgentSelectionStrategy = Embedding</c> changed nothing at runtime
/// while the documentation listed configurable agent selection as a shipped feature. This is
/// the call site that was missing.
/// </para>
/// <para>
/// <see cref="AgentSelectionStrategyKind.FirstFit"/> — the default — keeps round-robin, which
/// is what every crew has always run: it spreads unassigned tasks across the roster instead of
/// piling them on the first agent. The semantic and skill strategies are consulted only for a
/// task that declares no <c>agent:</c>, and only when the crew carries more than one agent to
/// choose between. A selection that fails, or that names an agent the crew does not carry,
/// falls back to round-robin with a warning rather than to an exception: agent choice is a
/// heuristic, and a crew must not die because an embedding endpoint did.
/// </para>
/// </summary>
public sealed partial class TaskAgentSelector
{
    /// <summary>Round-robin only — the shape used when nothing is registered (tests, direct construction).</summary>
    public static TaskAgentSelector RoundRobin { get; } = new();

    private readonly AgentSelectionStrategyKind _kind;
    private readonly IAgentSelectionService? _selection;
    private readonly IEmbeddingService? _embedder;
    private readonly ILogger? _logger;

    /// <summary>Initializes a selector from the configured strategy and its collaborators.</summary>
    public TaskAgentSelector(
        IOptions<OrkeonApplicationOptions>? options = null,
        IAgentSelectionService? selection = null,
        IEmbeddingService? embedder = null,
        ILogger<TaskAgentSelector>? logger = null)
    {
        _kind = options?.Value?.AgentSelectionStrategy ?? AgentSelectionStrategyKind.FirstFit;
        _selection = selection;
        _embedder = embedder;
        _logger = logger;
    }

    /// <summary>
    /// The agent for <paramref name="task"/>, advancing <paramref name="fallbackIndex"/> so a
    /// crew mixing assigned and unassigned tasks spreads the unassigned ones as before.
    /// </summary>
    public async ValueTask<DomainAgent> ForTaskAsync(
        Orkeon.Domain.Task.CrewTask task,
        IReadOnlyList<DomainAgent> agents,
        int fallbackIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(agents);
        if (agents.Count == 0)
            throw new ArgumentException("A crew must carry at least one agent to run a task.", nameof(agents));

        // A declared agent is a decision, not a hint: no strategy overrides it.
        if (task.AssignedAgent is { } assigned && agents.FirstOrDefault(a => a.Id == assigned) is { } declared)
            return declared;

        if (_kind == AgentSelectionStrategyKind.FirstFit
            || _selection is null
            || _embedder is null
            || agents.Count == 1)
        {
            return TaskAgentSelection.ForTask(task, agents, fallbackIndex);
        }

        return await SelectSemanticallyAsync(task, agents, fallbackIndex, cancellationToken).ConfigureAwait(false);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Agent choice is a heuristic over an external embedding backend: any failure degrades to round-robin with a warning rather than killing the crew run.")]
    private async ValueTask<DomainAgent> SelectSemanticallyAsync(
        Orkeon.Domain.Task.CrewTask task,
        IReadOnlyList<DomainAgent> agents,
        int fallbackIndex,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _selection!
                .SelectBestAgentAsync(agents, task, _embedder!, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsSuccess
                && result.SelectedAgentId is { } selectedId
                && agents.FirstOrDefault(a => a.Id == selectedId) is { } selected)
            {
                LogSelected(_logger, _kind.ToString(), selected.Role, result.ConfidenceScore);
                return selected;
            }

            LogNoSelection(_logger, _kind.ToString(), result.Reason ?? "no agent matched");
        }
        // Only the CALLER's cancellation is a cancellation. An HTTP timeout inside the
        // embedding backend also surfaces as TaskCanceledException — and definitionally is not
        // the crew's token, since the adapter passes CancellationToken.None down to the
        // provider. Rethrowing it unconditionally sent a stalled endpoint straight past the
        // degradation below, killing the crew and dispatching CrewHookStatus.Canceled, which
        // tells the operator the user cancelled a run the user never touched.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogSelectionFailed(_logger, ex, _kind.ToString());
        }

        return TaskAgentSelection.ForTask(task, agents, fallbackIndex);
    }

    private static void LogSelected(ILogger? logger, string strategy, string role, double confidence)
    {
        if (logger is not null)
            SelectedMessage(logger, strategy, role, confidence);
    }

    private static void LogNoSelection(ILogger? logger, string strategy, string reason)
    {
        if (logger is not null)
            NoSelectionMessage(logger, strategy, reason);
    }

    private static void LogSelectionFailed(ILogger? logger, Exception ex, string strategy)
    {
        if (logger is not null)
            SelectionFailedMessage(logger, ex, strategy);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Agent selection ({Strategy}) chose '{Role}' (confidence {Confidence:F2})")]
    private static partial void SelectedMessage(ILogger logger, string strategy, string role, double confidence);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Agent selection ({Strategy}) returned no agent ({Reason}) — falling back to round-robin")]
    private static partial void NoSelectionMessage(ILogger logger, string strategy, string reason);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "Agent selection ({Strategy}) failed — falling back to round-robin")]
    private static partial void SelectionFailedMessage(ILogger logger, Exception ex, string strategy);
}
