using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel;
using Orkeon.Infrastructure.LLMs.Capabilities;
using Orkeon.Infrastructure.LLMs.Replay;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>The optional host services an <see cref="LlmProviderFactory"/> hands to every provider it builds.</summary>
public sealed class LlmProviderFactoryOptions
{
    /// <summary>
    /// The host's usage receiver: every provider built is metered for it. Null - no host
    /// listening - builds the providers unmetered.
    /// </summary>
    public ILlmUsageSink? UsageSink { get; init; }

    /// <summary>
    /// The host's limiter (<c>RateLimiting</c>): every call of a provider built takes one of its
    /// leases. Null builds the providers unlimited.
    /// </summary>
    public ILlmRateLimiter? RateLimiter { get; init; }

    /// <summary>
    /// The host's budgets (<c>CostTracking</c>): every call of a provider built is recorded against them, and a
    /// call whose crew or agent has spent its budget is refused before it starts. Null enforces none.
    /// </summary>
    public ICostBudgetManager? Budget { get; init; }

    /// <summary>The host's call observer: every call of a provider built is announced to it. Null observes nothing.</summary>
    public ILlmCallObserver? CallObserver { get; init; }

    /// <summary>Estimates the tokens the observer is told; null uses <see cref="SimpleTokenCounter"/>.</summary>
    public ITokenCounter? TokenCounter { get; init; }

    /// <summary>The per-model table (FWK-21) every provider built reads; null uses the built-in one.</summary>
    public IModelCapabilityRegistry? Capabilities { get; init; }

    /// <summary>
    /// Record or replay (FWK-11): every provider built is wrapped, under the call-mode layer, in a decorator that writes its
    /// calls to the cassette or answers them from it. Null, or <see cref="LlmReplayMode.Off"/>, adds no layer.
    /// </summary>
    public LlmReplayOptions? Replay { get; init; }
}
