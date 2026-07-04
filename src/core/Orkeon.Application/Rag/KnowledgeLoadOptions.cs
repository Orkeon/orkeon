namespace Orkeon.Application.Rag;

/// <summary>
/// Options for loading knowledge from sources.
/// </summary>
public record KnowledgeLoadOptions(
    bool ForceReload = false,
    int? MaxItems = null,
    DateTime? ModifiedAfter = null,
    string? Filter = null);
