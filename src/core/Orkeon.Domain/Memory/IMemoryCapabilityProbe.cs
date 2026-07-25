namespace Orkeon.Domain.Memory;

/// <summary>
/// Implemented by <see cref="IMemoryProvider"/> wrappers (decorators) whose effective
/// capabilities depend on the wrapped provider. C# offers no conditional interface
/// implementation, so a decorator must statically implement every capability interface it may
/// forward; this probe lets it report which of those capabilities are <em>actually</em>
/// available on the wrapped chain.
/// </summary>
/// <remarks>
/// Consumers should not call capability members on a provider implementing this interface
/// without first consulting it — either directly or, preferably, through
/// <see cref="MemoryCapabilityExtensions.TryGetCapability{TCapability}"/>, which combines the
/// pattern-matching check and the probe in one canonical discovery step.
/// </remarks>
public interface IMemoryCapabilityProbe
{
    /// <summary>
    /// Indicates whether the capability <typeparamref name="TCapability"/> is effectively
    /// available (i.e. calls to its members will be forwarded rather than throw
    /// <see cref="NotSupportedException"/>).
    /// </summary>
    /// <typeparam name="TCapability">
    /// A capability interface such as <see cref="IScoredVectorSearch"/>,
    /// <see cref="IBatchUpsert"/> or <see cref="IHybridSearchCapable"/>.
    /// </typeparam>
    bool HasCapability<TCapability>() where TCapability : class;
}
