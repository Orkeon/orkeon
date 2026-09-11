using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Per-LLM-call rate-limit lease acquisition shared by the three execution loops.
/// Extracted verbatim from <see cref="ExecutionOrchestrator"/> (R4.1).
/// Leases are acquired/released per LLM call, NOT held across tool execution —
/// this prevents deadlocks when tools (e.g. delegate_work) trigger nested agent
/// executions that need the same concurrency slot.
/// </summary>
internal sealed class LlmCallGate
{
    private readonly ILogger _logger;
    private readonly IBasicLlmProvider _llmProvider;
    private readonly ILlmRateLimiter? _rateLimiter;

    /// <summary>The provider's own name, for the <c>gen_ai.provider.name</c> attribute of the spans.</summary>
    internal string ProviderName => _llmProvider.Name;

    internal LlmCallGate(ILogger logger, IBasicLlmProvider llmProvider, ILlmRateLimiter? rateLimiter)
    {
        _logger = logger;
        _llmProvider = llmProvider;
        _rateLimiter = rateLimiter;
    }

    /// <summary>
    /// Acquires the rate limit lease for a single LLM call.
    /// Returns the lease (IDisposable) that MUST be disposed after the HTTP call completes
    /// but BEFORE tool execution begins.
    /// Returns null if no rate limiter is configured.
    /// </summary>
    internal async System.Threading.Tasks.Task<IDisposable?> AcquireLlmLeaseAsync(
        DomainAgent agent, CancellationToken cancellationToken)
    {
        if (_rateLimiter == null) return null;

        var acquisition = await _rateLimiter.AcquireAsync(
            _llmProvider.Name, agent.Role.Value, cancellationToken).ConfigureAwait(false);

        if (!acquisition.IsAcquired)
        {
            ExecutionLog.LogRateLimitExceeded(_logger, agent.Role, _llmProvider.Name, acquisition.DenialReason ?? "unknown");
            throw new InvalidOperationException(
                $"LLM rate limit exceeded for agent '{agent.Role}' on provider '{_llmProvider.Name}': {acquisition.DenialReason}");
        }

        return acquisition.Lease;
    }
}
