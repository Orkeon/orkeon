using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Domain.Memory;

/// <summary>
/// Canonical discovery entry point for the optional memory capabilities
/// (<see cref="IScoredVectorSearch"/>, <see cref="IBatchUpsert"/>,
/// <see cref="IHybridSearchCapable"/>, <see cref="ICollectionAwareMemory"/>).
/// </summary>
public static class MemoryCapabilityExtensions
{
    /// <summary>
    /// Attempts to obtain the capability <typeparamref name="TCapability"/> from a provider.
    /// Equivalent to <c>provider is TCapability</c> for concrete providers, but additionally
    /// honours <see cref="IMemoryCapabilityProbe"/> so that decorators (which must statically
    /// implement every capability interface they may forward) only advertise the capabilities
    /// their wrapped provider actually has.
    /// </summary>
    /// <typeparam name="TCapability">The capability interface to obtain.</typeparam>
    /// <param name="provider">The memory provider (possibly decorated).</param>
    /// <param name="capability">The capability instance when available; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the capability is effectively available.</returns>
    public static bool TryGetCapability<TCapability>(
        this IMemoryProvider provider,
        [NotNullWhen(true)] out TCapability? capability)
        where TCapability : class
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (provider is TCapability candidate
            && (provider is not IMemoryCapabilityProbe probe || probe.HasCapability<TCapability>()))
        {
            capability = candidate;
            return true;
        }

        capability = null;
        return false;
    }
}
