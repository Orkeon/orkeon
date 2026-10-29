namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>One answer of a decision.</summary>
public sealed record DecisionAnswerValue
{
    /// <summary>Gets the chosen option.</summary>
    public string? Chosen { get; init; }

    /// <summary>Gets the probability of each option.</summary>
    public IReadOnlyDictionary<string, double> Probabilities { get; init; } = new Dictionary<string, double>();

    /// <summary>Gets the weighted score of a score question.</summary>
    public double? Score { get; init; }

    /// <summary>Gets the yes probability of a yes/no question.</summary>
    public double? Probability { get; init; }

    /// <summary>Gets the confidence.</summary>
    public double Confidence { get; init; }
}

/// <summary>The outcome of a decision task.</summary>
public sealed record DecisionOutcome
{
    /// <summary>The key of the compact JSON that marks a defaulted outcome: no question takes that name.</summary>
    public const string DefaultedKey = "defaulted";

    /// <summary>Gets the answers by question name.</summary>
    public required IReadOnlyDictionary<string, DecisionAnswerValue> Answers { get; init; }

    /// <summary>Gets whether the fallback answered.</summary>
    public bool IsDefaulted { get; init; }

    /// <summary>Creates a defaulted outcome.</summary>
    public static DecisionOutcome Defaulted(IReadOnlyDictionary<string, DecisionAnswerValue> answers) =>
        new() { Answers = answers, IsDefaulted = true };

    /// <summary>Serialises the outcome.</summary>
    public string ToCompactJson()
    {
        var json = new Dictionary<string, object>();
        foreach (var (question, answer) in Answers)
        {
            var fields = new Dictionary<string, object>();
            if (answer.Chosen is not null)
                fields["chosen"] = answer.Chosen;
            if (answer.Score is not null)
                fields["score"] = answer.Score;
            if (answer.Probability is not null)
                fields["probability"] = answer.Probability;
            fields["probabilities"] = answer.Probabilities;
            fields["confidence"] = answer.Confidence;
            json[question] = fields;
        }

        if (IsDefaulted)
            json[DefaultedKey] = true;
        return System.Text.Json.JsonSerializer.Serialize(json);
    }
}
