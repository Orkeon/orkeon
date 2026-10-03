using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Infrastructure.LLMs;

/// <summary>
/// The one function every entrance of the metered path applies to the provider it hands the
/// runtime (GAP-38): the meter (<see cref="MeteredLlmProvider"/>, STUDIO-42) and, around it, the
/// host's limiter (<see cref="RateLimitedLlmProvider"/>). The entrances are the provider factory,
/// <c>AddOrkeonLlmProvider</c> and <c>AddOrkeonLlmProfile</c>, the profile registry's
/// <c>ForProvider</c>, <c>ManagerLlmResolver</c> and <c>SequentialCrewOrchestrator</c> — the points
/// where a provider the host's own code built enters the runtime. A provider passed through two
/// of them is metered and limited once.
/// </summary>
internal static class LlmProviderEntrance
{
    /// <summary>
    /// <paramref name="provider"/> metered for <paramref name="usageSink"/> and limited by
    /// <paramref name="rateLimiter"/> — each left out when it is null, and neither applied twice.
    /// </summary>
    /// <param name="provider">The provider entering the runtime.</param>
    /// <param name="usageSink">The host's token meter; null meters nothing.</param>
    /// <param name="rateLimiter">The host's limiter; null limits nothing.</param>
    /// <returns>The provider the runtime calls.</returns>
    public static ILlmProvider Enter(ILlmProvider provider, ILlmUsageSink? usageSink, ILlmRateLimiter? rateLimiter)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (provider is RateLimitedLlmProvider)
            return provider;

        return RateLimitedLlmProvider.Wrap(MeteredLlmProvider.Wrap(provider, usageSink), rateLimiter);
    }
}
