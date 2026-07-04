namespace Orkeon.Analysis.Abstractions.Models;

public sealed record VectorSearchHit(
    string Id,
    string Text,
    float Score,
    VectorMetadata Metadata);
