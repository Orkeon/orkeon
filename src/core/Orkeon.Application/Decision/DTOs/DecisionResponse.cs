using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Orkeon.Application.Decision.DTOs;

/// <summary>The answer of <see cref="Interfaces.Ports.IDecisionModel.DecideAsync"/>.</summary>
public sealed record DecisionResponse
{
    /// <summary>Gets the model that answered, which may differ from the alias requested.</summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>Gets the answers, by the names of the questions.</summary>
    [JsonPropertyName("answers")]
    public required ImmutableDictionary<string, DecisionAnswer> Answers { get; init; }

    /// <summary>Gets the tokens the request used.</summary>
    [JsonPropertyName("usage")]
    public DecisionUsage Usage { get; init; } = new();

    /// <summary>
    /// Returns the answer to question <paramref name="name"/> as a <typeparamref name="TAnswer"/>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No answer has that name.</exception>
    /// <exception cref="InvalidOperationException">The answer is of another type.</exception>
    public TAnswer Get<TAnswer>(string name) where TAnswer : DecisionAnswer
    {
        if (!Answers.TryGetValue(name, out var answer))
            throw new KeyNotFoundException($"The decision model returned no answer named '{name}'.");
        return answer as TAnswer ?? throw new InvalidOperationException(
            $"The answer '{name}' is a {answer.GetType().Name}, not a {typeof(TAnswer).Name}.");
    }
}

/// <summary>The tokens a decision request used. Some providers bill the input only.</summary>
public sealed record DecisionUsage
{
    /// <summary>Gets the billable input tokens.</summary>
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    /// <summary>Gets the output tokens.</summary>
    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }
}

/// <summary>A model or alias a decision provider accepts.</summary>
public sealed record DecisionModelDto
{
    /// <summary>Gets the name a request's model takes.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Gets the provider's description of the model.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>Gets the release date, <c>YYYY-MM-DD</c>.</summary>
    [JsonPropertyName("release_date")]
    public string ReleaseDate { get; init; } = string.Empty;
}
