using System.Text.Json.Serialization;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Tools.Rag;

/// <summary>Typed request of <see cref="RagSearchTool"/>.</summary>
public record RagSearchRequest
{
    /// <summary>Gets the question.</summary>
    [JsonPropertyName("question")]
    public string Question { get; init; } = "";

    /// <summary>Gets the number of chunks to retrieve.</summary>
    [JsonPropertyName("top_k")]
    public int TopK { get; init; } = 3;

    /// <summary>Gets the collection selector.</summary>
    [JsonPropertyName("collection")]
    public string? Collection { get; init; }
}

/// <summary>Typed response of <see cref="RagSearchTool"/>.</summary>
public record RagSearchResponse
{
    /// <summary>Gets the answer text.</summary>
    [JsonPropertyName("answer")]
    public string Answer { get; init; } = "";

    /// <summary>Gets the citations.</summary>
    [JsonPropertyName("citations")]
    public IReadOnlyList<Citation> Citations { get; init; } = [];
}
