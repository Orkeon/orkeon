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
    private readonly ILogger? _verboseLogger;

    /// <summary>The provider's own name, for the <c>gen_ai.provider.name</c> attribute of the spans.</summary>
    internal string ProviderName => _llmProvider.Name;

    /// <param name="logger">Where the line of a call that waited goes.</param>
    /// <param name="llmProvider">The provider the calls go to.</param>
    /// <param name="time">The clock the windows count on; <see cref="TimeProvider.System"/> when null.</param>
    /// <param name="hostAgentLimit">The host's per-agent cap (<c>RateLimiting:AgentRequestsPerMinute</c>); null sets none.</param>
    /// <param name="verboseLogger">Where the line goes for a verbose agent or crew; null keeps it on <paramref name="logger"/>.</param>
    internal LlmCallGate(ILogger logger, IBasicLlmProvider llmProvider, TimeProvider? time = null, int? hostAgentLimit = null, ILogger? verboseLogger = null)
    {
        _logger = logger;
        _llmProvider = llmProvider;
        _time = time ?? TimeProvider.System;
        _hostAgentLimit = hostAgentLimit;
        _verboseLogger = verboseLogger;
    }

    /// <summary>
    /// Waits until <paramref name="agent"/> may make one more model call — at once when neither it nor
    /// the crew in progress declares a limit — and counts the call.
    /// </summary>
    /// <exception cref="OperationCanceledException">The call was cancelled while it waited; it counted nothing.</exception>
    internal System.Threading.Tasks.Task WaitTurnAsync(DomainAgent agent, CancellationToken cancellationToken) =>
        WaitTurnAsync(agent, _hostAgentLimit, _time, _logger, _verboseLogger, cancellationToken);

    /// <summary>
    /// The same wait for a model call made outside an agent loop - the <c>without:</c> relay of a decision task
    /// (#430) -: the agent's window and the crew's, the host's per-agent cap, and the line of a call that waited.
    /// </summary>
    /// <param name="agent">The agent the call is made for.</param>
    /// <param name="hostAgentLimit">The host's per-agent cap; null sets none.</param>
    /// <param name="time">The clock the windows count on.</param>
    /// <param name="logger">Where the line of a call that waited goes.</param>
    /// <param name="verboseLogger">Where the line goes for a verbose agent or crew; null keeps it on <paramref name="logger"/>.</param>
    /// <param name="cancellationToken">The call's token.</param>
    /// <exception cref="OperationCanceledException">The call was cancelled while it waited; it counted nothing.</exception>
    internal static async System.Threading.Tasks.Task WaitTurnAsync(
        DomainAgent agent, int? hostAgentLimit, TimeProvider time, ILogger logger, ILogger? verboseLogger, CancellationToken cancellationToken)
    {
        var turn = await RequestRates.WaitTurnAsync(agent, hostAgentLimit, time, cancellationToken).ConfigureAwait(false);
        if (!turn.HasWaited)
            return;

        var turnLogger = ExecutionLog.TurnLogger(logger, verboseLogger, agent);
        ExecutionLog.LogWaitedForTurn(turnLogger, agent.Role, turn.Waited.TotalSeconds, turn.Limit ?? string.Empty);
    }
}
