using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Orkeon.Application.Decision.DTOs;

/// <summary>A decision model's typed answer, of the type of the question it answers.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ChoiceAnswer), "choice")]
[JsonDerivedType(typeof(ScoreAnswer), "score")]
[JsonDerivedType(typeof(NoulAnswer), "noul")]
[JsonDerivedType(typeof(BoxAnswer), "box")]
public abstract record DecisionAnswer;

/// <summary>The answer to a <see cref="ChoiceQuestion"/>.</summary>
public sealed record ChoiceAnswer : DecisionAnswer
{
    /// <summary>Gets the option with the highest probability.</summary>
    [JsonPropertyName("choice")]
    public required string Choice { get; init; }

    /// <summary>Gets the confidence in <see cref="Choice"/>, from 0 to 1.</summary>
    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    /// <summary>Gets the probability of each option, by name; they sum to about 1.</summary>
    [JsonPropertyName("probabilities")]
    public required ImmutableDictionary<string, double> Probabilities { get; init; }
}

/// <summary>The answer to a <see cref="ScoreQuestion"/>.</summary>
public sealed record ScoreAnswer : DecisionAnswer
{
    /// <summary>
    /// Gets the expected score: the probability-weighted average of the levels, which may fall
    /// between two of them.
    /// </summary>
    [JsonPropertyName("score")]
    public required double Score { get; init; }

    /// <summary>Gets the confidence in <see cref="Score"/>, from 0 to 1.</summary>
    [JsonPropertyName("confidence")]
    public required double Confidence { get; init; }

    /// <summary>Gets each level's description, keyed by the level as the probabilities are.</summary>
    [JsonPropertyName("legend")]
    public ImmutableDictionary<string, JsonNode?> Legend { get; init; } = ImmutableDictionary<string, JsonNode?>.Empty;

    /// <summary>Gets the probability of each level; they sum to about 1.</summary>
    [JsonPropertyName("probabilities")]
    public required ImmutableDictionary<string, double> Probabilities { get; init; }
}

/// <summary>The answer to a <see cref="NoulQuestion"/>.</summary>
public sealed record NoulAnswer : DecisionAnswer
{
    /// <summary>
    /// Gets the probability of yes, from 0 to 1: near 1 favours yes, near 0 favours no, near
    /// 0.5 is uncertain.
    /// </summary>
    [JsonPropertyName("noul")]
    public required double Noul { get; init; }
}

/// <summary>The answer to a <see cref="BoxQuestion"/>.</summary>
public sealed record BoxAnswer : DecisionAnswer
{
    /// <summary>Gets the box's edges, as fractions of the image's height and width.</summary>
    [JsonPropertyName("box")]
    public required BoxCoordinates Box { get; init; }

    /// <summary>Gets the confidence in <see cref="Box"/>, from 0 to 1; null when the model gives none.</summary>
    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }
}

/// <summary>A box's edges, each a fraction from 0 to 1 of the image's height or width.</summary>
public sealed record BoxCoordinates
{
    /// <summary>Gets the top edge, from the top of the image.</summary>
    [JsonPropertyName("top")]
    public required double Top { get; init; }

    /// <summary>Gets the bottom edge, from the top of the image.</summary>
    [JsonPropertyName("bottom")]
    public required double Bottom { get; init; }

    /// <summary>Gets the left edge, from the left of the image.</summary>
    [JsonPropertyName("left")]
    public required double Left { get; init; }

    /// <summary>Gets the right edge, from the left of the image.</summary>
    [JsonPropertyName("right")]
    public required double Right { get; init; }
}
