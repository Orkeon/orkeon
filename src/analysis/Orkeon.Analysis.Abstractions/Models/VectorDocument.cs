namespace Orkeon.Analysis.Abstractions.Models;

public sealed record VectorDocument(
    string Id,
    string Text,
    ReadOnlyMemory<float> Embedding,
    VectorMetadata Metadata);
