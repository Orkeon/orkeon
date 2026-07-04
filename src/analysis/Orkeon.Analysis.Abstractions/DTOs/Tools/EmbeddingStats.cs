using System.Text.Json.Serialization;

namespace Orkeon.Analysis.Abstractions.DTOs.Tools;

public sealed record EmbeddingStats
{
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }  // "OpenAI" | "Ollama" | "None"

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("dimensions")]
    public required int Dimensions { get; init; }

    [JsonPropertyName("nodes_embedded")]
    public required int NodesEmbedded { get; init; }

    [JsonPropertyName("nodes_skipped")]
    public required int NodesSkipped { get; init; }

    [JsonPropertyName("elapsed")]
    public required TimeSpan Elapsed { get; init; }
}
