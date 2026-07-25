namespace Orkeon.Rag.Factories;

/// <summary>
/// Thrown when a named RAG component cannot be resolved by a
/// <see cref="NamedRagComponentFactory{TComponent}"/>. The message always lists
/// the known names and aliases — resolution failures are never silent.
/// </summary>
public sealed class RagComponentNotFoundException : InvalidOperationException
{
    /// <summary>Initializes the exception with the failed lookup details.</summary>
    /// <param name="componentKind">Human-readable component kind (e.g. "chunking strategy").</param>
    /// <param name="requestedName">The name that failed to resolve (as requested, un-normalized).</param>
    /// <param name="knownNames">Registered names and aliases at the time of the lookup.</param>
    public RagComponentNotFoundException(
        string componentKind,
        string requestedName,
        IReadOnlyCollection<string> knownNames)
        : base(BuildMessage(componentKind, requestedName, knownNames))
    {
        RequestedName = requestedName;
        KnownNames = knownNames;
    }

    /// <summary>Creates an instance without lookup details.</summary>
    public RagComponentNotFoundException()
    {
        RequestedName = string.Empty;
        KnownNames = [];
    }

    /// <summary>Creates an instance with a raw message.</summary>
    public RagComponentNotFoundException(string message)
        : base(message)
    {
        RequestedName = string.Empty;
        KnownNames = [];
    }

    /// <summary>Creates an instance with a raw message and an inner exception.</summary>
    public RagComponentNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
        RequestedName = string.Empty;
        KnownNames = [];
    }

    /// <summary>The name that failed to resolve, exactly as requested.</summary>
    public string RequestedName { get; }

    /// <summary>Names and aliases registered at the time of the lookup.</summary>
    public IReadOnlyCollection<string> KnownNames { get; }

    private static string BuildMessage(
        string componentKind,
        string requestedName,
        IReadOnlyCollection<string> knownNames)
    {
        var known = knownNames.Count == 0
            ? "(none registered)"
            : string.Join(", ", knownNames.Order(StringComparer.Ordinal));
        return $"Unknown {componentKind} '{requestedName}'. Known names and aliases: {known}.";
    }
}
