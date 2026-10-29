using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Orkeon.Application.Decision.DTOs;

/// <summary>
/// A typed question a decision model answers about the state of a <see cref="DecisionRequest"/>.
/// Instructions and criteria accept a string, an object or an array: a <see cref="JsonNode"/>
/// carries them unchanged.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ChoiceQuestion), "choice")]
[JsonDerivedType(typeof(ScoreQuestion), "score")]
[JsonDerivedType(typeof(NoulQuestion), "noul")]
[JsonDerivedType(typeof(BoxQuestion), "box")]
public abstract record DecisionQuestion
{
    /// <summary>Gets what the model should decide, rate, judge or locate.</summary>
    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? Instructions { get; init; }
}

/// <summary>Selects one option out of a set: answered by a <see cref="ChoiceAnswer"/>.</summary>
public sealed record ChoiceQuestion : DecisionQuestion
{
    /// <summary>
    /// Gets the options, by name, each with a description of when it applies (null: the name
    /// alone). One to 255 options.
    /// </summary>
    [JsonPropertyName("criteria")]
    public required ImmutableDictionary<string, JsonNode?> Criteria { get; init; }
}

/// <summary>Rates the state on an ordered rubric: answered by a <see cref="ScoreAnswer"/>.</summary>
public sealed record ScoreQuestion : DecisionQuestion
{
    /// <summary>
    /// Gets the level descriptions, lowest first: a level's position is its score, from zero.
    /// One to ten levels.
    /// </summary>
    [JsonPropertyName("criteria")]
    public required ImmutableList<JsonNode> Criteria { get; init; }
}

/// <summary>A yes/no question or statement: answered by a <see cref="NoulAnswer"/>.</summary>
public sealed record NoulQuestion : DecisionQuestion
{
    /// <summary>Gets what counts as yes and as no; null leaves it to the instructions.</summary>
    [JsonPropertyName("criteria")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NoulCriteria? Criteria { get; init; }
}

/// <summary>What counts as a yes and as a no for a <see cref="NoulQuestion"/>.</summary>
public sealed record NoulCriteria
{
    /// <summary>Gets what counts as a yes answer.</summary>
    [JsonPropertyName("true")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? True { get; init; }

    /// <summary>Gets what counts as a no answer.</summary>
    [JsonPropertyName("false")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? False { get; init; }
}

/// <summary>
/// Locates what the instructions describe in the request's image: answered by a
/// <see cref="BoxAnswer"/>. Needs an image among the request's assets.
/// </summary>
public sealed record BoxQuestion : DecisionQuestion;
