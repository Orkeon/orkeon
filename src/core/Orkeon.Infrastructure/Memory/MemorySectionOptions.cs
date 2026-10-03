namespace Orkeon.Infrastructure.Memory;

/// <summary>
/// The root <c>Memory</c> section (GAP-08, GAP-40): the TYPE of the application-wide memory provider,
/// whose connection comes from that provider's own section (<c>Orkeon:Redis</c>, …). Bound and judged
/// at the host's start by <c>AddOrkeonInfrastructure()</c>: a type the factory does not serve, or one
/// whose section lacks what it needs, refuses the start.
/// </summary>
public sealed class MemorySectionOptions
{
    /// <summary>The configuration section: <c>Memory</c>, at the root.</summary>
    public const string SectionName = "Memory";

    /// <summary>
    /// The provider type — <c>inmemory</c>, <c>redis</c>, <c>sqlite</c>, <c>chromadb</c>,
    /// <c>pinecone</c>, <c>lancedb</c>, or an alias. Unset is the in-memory provider.
    /// </summary>
    public string? Provider { get; set; }
}
