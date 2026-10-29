using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Orkeon.Application.Decision.DTOs;

/// <summary>
/// The input of <see cref="Interfaces.Ports.IDecisionModel.DecideAsync"/>: a state and the named
/// questions to answer about it. The questions are evaluated in parallel and in isolation, so
/// asking several in one request costs little more than asking one.
/// </summary>
public sealed record DecisionRequest
{
    /// <summary>Gets the model or alias; null takes the provider's configured default.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>Gets the content every question refers to: a string, an object or an array.</summary>
    [JsonPropertyName("state")]
    public required JsonNode State { get; init; }

    /// <summary>Gets the questions, by the name their answers come back under. At least one.</summary>
    [JsonPropertyName("questions")]
    public required ImmutableDictionary<string, DecisionQuestion> Questions { get; init; }

    /// <summary>
    /// Gets the images, by the placeholder that stands for each one wherever it appears as or
    /// inside a string of the state, the instructions or the criteria. Empty: no image.
    /// </summary>
    [JsonPropertyName("assets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableDictionary<string, DecisionImage>? Assets { get; init; }

    /// <summary>
    /// Gets a value indicating whether the request may use the provider's long context, slower
    /// and less accurate than the default one. Sent as a header, never in the body.
    /// </summary>
    [JsonIgnore]
    public bool LongContext { get; init; }

    /// <summary>Gets a value indicating whether the request carries an image or asks a <see cref="BoxQuestion"/>.</summary>
    [JsonIgnore]
    public bool UsesImages => Assets is { Count: > 0 } || Questions.Values.Any(q => q is BoxQuestion);
}

/// <summary>An image of a <see cref="DecisionRequest"/>, inline.</summary>
public sealed record DecisionImage
{
    /// <summary>Gets the asset kind; images are the only one.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "image";

    /// <summary>Gets the image format: <c>png</c>, <c>jpg</c> or <c>jpeg</c>.</summary>
    [JsonPropertyName("format")]
    public required string Format { get; init; }

    /// <summary>Gets the image bytes, base64-encoded.</summary>
    [JsonPropertyName("data_base64")]
    public required string DataBase64 { get; init; }
}
