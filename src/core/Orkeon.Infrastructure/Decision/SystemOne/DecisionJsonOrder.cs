using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Application.Decision.DTOs;

namespace Orkeon.Infrastructure.Decision.SystemOne;

/// <summary>
/// Writes a <see cref="DecisionRequest"/> and its answers as JSON in a fixed order (#430): the maps of the port,
/// <see cref="DecisionRequest.Questions"/>, <see cref="ChoiceQuestion.Criteria"/>, <see cref="DecisionRequest.Assets"/>
/// and the answers' probabilities, are <see cref="ImmutableDictionary{TKey, TValue}"/>, which enumerate in the hash
/// order of their keys, and that order changes from one process to the next. The order a task declared does not
/// travel in those shipped types, so the names are written in their ordinal order: the same body for the same
/// request in every process, a prompt cache that hits and a recorded mock that matches.
/// </summary>
internal static class DecisionJsonOrder
{
    /// <summary>The request as it is sent: questions, each choice's criteria and the assets by ordinal name.</summary>
    internal static JsonNode Request(DecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = JsonSerializer.SerializeToNode(request)!;
        if (body["questions"] is JsonObject questions)
            Questions(questions);
        if (body["assets"] is JsonObject assets)
            Sort(assets);
        return body;
    }

    /// <summary>The questions by ordinal name, each choice's criteria by ordinal name too.</summary>
    internal static JsonObject Questions(ImmutableDictionary<string, DecisionQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        var json = JsonSerializer.SerializeToNode(questions)!.AsObject();
        Questions(json);
        return json;
    }

    /// <summary>The answers by ordinal question name, each one's probabilities and legend by ordinal key.</summary>
    internal static JsonObject Answers(ImmutableDictionary<string, DecisionAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var json = JsonSerializer.SerializeToNode(answers)!.AsObject();
        Sort(json);
        foreach (var (_, answer) in json)
        {
            if (answer?["probabilities"] is JsonObject probabilities)
                Sort(probabilities);
            if (answer?["legend"] is JsonObject legend)
                Sort(legend);
        }

        return json;
    }

    private static void Questions(JsonObject questions)
    {
        Sort(questions);
        foreach (var (_, question) in questions)
        {
            // A score's criteria are a list, in the order of its levels: only a choice's are a map.
            if (question?["criteria"] is JsonObject criteria && (string?)question["type"] == "choice")
                Sort(criteria);
        }
    }

    /// <summary>Puts the properties of <paramref name="json"/> in the ordinal order of their names, in place.</summary>
    internal static void Sort(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var properties = json.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        json.Clear();
        foreach (var (name, value) in properties)
            json[name] = value;
    }
}
