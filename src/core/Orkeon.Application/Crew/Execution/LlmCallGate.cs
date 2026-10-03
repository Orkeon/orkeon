using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The point every model call of an agent goes through, shared by the three execution loops and
/// the correction round: each turn, the tool-free retry, the correction. It waits for the call's
/// turn in the agent's window and in the crew's (<see cref="RequestRates"/>, GAP-38) and says, on
/// one Information line, who waited, how long and under which limit. It takes no lease: the host's
/// <c>RateLimiting</c> is applied once, at the entrance of the provider the call goes to.
/// </summary>
/// <remarks>
/// Nothing is held past the call: a turn waits, then counts, before its call — never across a tool
/// call, so a tool that runs another agent's turn in the same run (<c>delegate_work</c>) waits in
/// that agent's window, never on its caller.
/// </remarks>
internal sealed class LlmCallGate
{
    private readonly ILogger _logger;
    private readonly IBasicLlmProvider _llmProvider;
    private readonly TimeProvider _time;
    private readonly int? _hostAgentLimit;

    /// <summary>The provider's own name, for the <c>gen_ai.provider.name</c> attribute of the spans.</summary>
    internal string ProviderName => _llmProvider.Name;

    /// <param name="logger">Where the line of a call that waited goes.</param>
    /// <param name="llmProvider">The provider the calls go to.</param>
    /// <param name="time">The clock the windows count on; <see cref="TimeProvider.System"/> when null.</param>
    /// <param name="hostAgentLimit">The host's per-agent cap (<c>RateLimiting:AgentRequestsPerMinute</c>); null sets none.</param>
    internal LlmCallGate(ILogger logger, IBasicLlmProvider llmProvider, TimeProvider? time = null, int? hostAgentLimit = null)
    {
        _logger = logger;
        _llmProvider = llmProvider;
        _time = time ?? TimeProvider.System;
        _hostAgentLimit = hostAgentLimit;
    }

    /// <summary>
    /// Waits until <paramref name="agent"/> may make one more model call — at once when neither it nor
    /// the crew in progress declares a limit — and counts the call.
    /// </summary>
    /// <exception cref="OperationCanceledException">The call was cancelled while it waited; it counted nothing.</exception>
    internal async System.Threading.Tasks.Task WaitTurnAsync(DomainAgent agent, CancellationToken cancellationToken)
    {
        var turn = await RequestRates.WaitTurnAsync(agent, _hostAgentLimit, _time, cancellationToken).ConfigureAwait(false);
        if (turn.HasWaited)
            ExecutionLog.LogWaitedForTurn(_logger, agent.Role, turn.Waited.TotalSeconds, turn.Limit ?? string.Empty);
    }
}
