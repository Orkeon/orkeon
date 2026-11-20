using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// What a decision task's <c>without:</c> relay is asked and how its answer is read (#430): the JSON schema
/// generated from the questions, the prompts, and the checks an answer passes before it becomes the same
/// <see cref="DecisionResponse"/> the decision model gives. The agent reports probabilities only; the rest is
/// derived here exactly as the model path derives it.
/// </summary>
internal static class DecisionRelayProtocol
{
    /// <summary>
    /// How far a distribution (the options of a choice, the levels of a score) may sum from 1 and still be
    /// accepted; it is then normalised. Stated in <c>docs/architecture/decision-models.md</c>.
    /// </summary>
    internal const double SumTolerance = 0.05;

    /// <summary>The <c>model</c> a relay answer reports: <c>relay:&lt;agent role&gt;</c>.</summary>
    internal static string ModelOf(DomainAgent agent) => "relay:" + agent.Role.Value;

    /// <summary>The JSON schema of the answer: one key per question, every key required, nothing else.</summary>
    internal static string Schema(TaskDecision decision)
    {
        var properties = new JsonObject();
        foreach (var question in decision.Questions)
            properties[question.Name] = QuestionSchema(question);

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = Names(decision.Questions.Select(q => q.Name)),
            ["additionalProperties"] = false,
        }.ToJsonString();
    }

    /// <summary>
    /// The request of one call: the agent's prompt (role, goal, backstory), the questions, the state and the
    /// relay's instructions; on the second call, why the first answer was refused.
    /// </summary>
    internal static DecisionRelayRequest Request(
        DomainAgent agent, string taskId, TaskDecision decision, DecisionRelay relay, JsonNode state, string? refusal)
    {
        var schema = Schema(decision);
        var prompt = UserPrompt(decision, relay, state, refusal);
        return new DecisionRelayRequest
        {
            Agent = agent,
            TaskId = taskId,
            SystemPrompt = SystemPrompt(agent),
            Prompt = prompt,
            PromptWithSchema = prompt + "\n\nThe JSON object follows this JSON Schema:\n" + schema +
                "\nWrite the JSON object alone: no code fence, no other text.",
            Schema = schema,
        };
    }

    /// <summary>
    /// Reads and checks an answer: JSON that parses, every question present, each value in [0, 1], each
    /// distribution summing to 1 within <see cref="SumTolerance"/>. The distributions are normalised, then
    /// <c>chosen</c>, <c>score</c> and <c>confidence</c> are derived.
    /// </summary>
    /// <exception cref="FormatException">The answer fails a check; the message says which.</exception>
    internal static DecisionResponse Parse(string text, TaskDecision decision, string model)
    {
        JsonObject answer;
        try
        {
            answer = JsonNode.Parse(Unfenced(text)) as JsonObject
                ?? throw new FormatException("the answer is not a JSON object");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"the answer is not valid JSON ({ex.Message})", ex);
        }

        var answers = ImmutableDictionary.CreateBuilder<string, DecisionAnswer>(StringComparer.Ordinal);
        foreach (var question in decision.Questions)
        {
            if (!answer.TryGetPropertyValue(question.Name, out var value) || value is null)
                throw new FormatException($"question '{question.Name}' is missing");
            answers[question.Name] = question switch
            {
                ChoiceQuestionSpec choice => Choice(question.Name, value, [.. choice.Options.Keys]),
                ScoreQuestionSpec score => Score(question.Name, value, score.Levels.Count),
                _ => Noul(question.Name, value),
            };
        }

        return new DecisionResponse { Model = model, Answers = answers.ToImmutable() };
    }

    private static ChoiceAnswer Choice(string question, JsonNode value, IReadOnlyList<string> options)
    {
        var probabilities = Distribution(question, value, options);
        var best = probabilities.OrderByDescending(p => p.Value).ThenBy(p => IndexOf(options, p.Key)).First();
        return new ChoiceAnswer { Choice = best.Key, Confidence = best.Value, Probabilities = probabilities };
    }

    private static ScoreAnswer Score(string question, JsonNode value, int levels)
    {
        var keys = Enumerable.Range(0, levels).Select(Position).ToList();
        var probabilities = Distribution(question, value, keys);

        // On the scale thresholds read: the weighted position of the levels, the first one counted 0.
        var score = Enumerable.Range(0, levels).Sum(i => i * probabilities[Position(i)]);
        return new ScoreAnswer { Score = score, Confidence = probabilities.Values.Max(), Probabilities = probabilities };
    }

    private static NoulAnswer Noul(string question, JsonNode value) =>
        new() { Noul = Probability(question, value, null) };

    private static ImmutableDictionary<string, double> Distribution(string question, JsonNode value, IReadOnlyList<string> keys)
    {
        if (value is not JsonObject given)
            throw new FormatException($"question '{question}' is not an object of probabilities");
        if (given.Select(p => p.Key).FirstOrDefault(k => !keys.Contains(k)) is { } unknown)
            throw new FormatException($"question '{question}' has no option '{unknown}'");

        var raw = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (!given.TryGetPropertyValue(key, out var p) || p is null)
                throw new FormatException($"question '{question}' gives no probability to '{key}'");
            raw[key] = Probability(question, p, key);
        }

        var sum = raw.Values.Sum();
        if (Math.Abs(sum - 1) > SumTolerance)
            throw new FormatException(
                $"the probabilities of question '{question}' sum to {sum.ToString("0.###", CultureInfo.InvariantCulture)}, not 1 (tolerance {SumTolerance.ToString(CultureInfo.InvariantCulture)})");

        return raw.ToImmutableDictionary(p => p.Key, p => p.Value / sum, StringComparer.Ordinal);
    }

    private static double Probability(string question, JsonNode value, string? key)
    {
        var where = key is null ? $"question '{question}'" : $"'{key}' of question '{question}'";
        double number;
        try
        {
            number = value.GetValue<double>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new FormatException($"{where} is not a number", ex);
        }

        if (!double.IsFinite(number) || number < 0 || number > 1)
            throw new FormatException($"{where} is {number.ToString(CultureInfo.InvariantCulture)}, outside [0, 1]");
        return number;
    }

    private static JsonObject QuestionSchema(DecisionQuestionSpec question) => question switch
    {
        ChoiceQuestionSpec choice => DistributionSchema(choice.Options.Keys),
        ScoreQuestionSpec score => DistributionSchema(Enumerable.Range(0, score.Levels.Count).Select(Position)),
        _ => ProbabilitySchema(),
    };

    private static JsonObject DistributionSchema(IEnumerable<string> keys)
    {
        var listed = keys.ToList();
        var properties = new JsonObject();
        foreach (var key in listed)
            properties[key] = ProbabilitySchema();
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = Names(listed),
            ["additionalProperties"] = false,
        };
    }

    private static JsonObject ProbabilitySchema() => new() { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 };

    private static JsonArray Names(IEnumerable<string> names) => [.. names.Select(n => (JsonNode)JsonValue.Create(n))];

    private static string SystemPrompt(DomainAgent agent)
    {
        var prompt = new StringBuilder()
            .Append("You are ").Append(agent.Role.Value).Append(".\n")
            .Append("Your goal: ").Append(agent.Goal.Value).Append('\n');
        if (agent.Backstory?.Value is { Length: > 0 } backstory)
            prompt.Append(backstory).Append('\n');
        return prompt
            .Append("Here you answer the questions of a decision with probabilities only, as one JSON object. ")
            .Append("You call no tool and write no other text.")
            .ToString();
    }

    private static string UserPrompt(TaskDecision decision, DecisionRelay relay, JsonNode state, string? refusal)
    {
        var prompt = new StringBuilder()
            .Append("The state you judge, the outputs of the tasks this decision reads, as JSON:\n")
            .Append(state.ToJsonString()).Append("\n\nThe questions:\n");
        foreach (var question in decision.Questions)
            AppendQuestion(prompt, question);

        if (relay.Instructions is { } instructions)
            prompt.Append('\n').Append(instructions).Append('\n');

        prompt
            .Append("\nAnswer with one JSON object that has one key per question, named as above:\n")
            .Append("- a choice: an object that gives every option a probability; they sum to 1.\n")
            .Append("- a score: an object that gives every level, keyed by its position, a probability; they sum to 1.\n")
            .Append("- a yes-no: a number, the probability that the statement is true.\n")
            .Append("Every probability is a number from 0 to 1.");
        if (refusal is not null)
            prompt.Append("\n\nYour previous answer was refused: ").Append(refusal).Append(". Answer again, following these rules.");
        return prompt.ToString();
    }

    private static void AppendQuestion(StringBuilder prompt, DecisionQuestionSpec question)
    {
        switch (question)
        {
            case ChoiceQuestionSpec choice:
                prompt.Append("- \"").Append(choice.Name).Append("\" (a choice)").Append(Said(choice.Instructions)).Append(" Options:\n");
                foreach (var (option, description) in choice.Options)
                    prompt.Append("  - \"").Append(option).Append("\": ").Append(description).Append('\n');
                break;
            case ScoreQuestionSpec score:
                prompt.Append("- \"").Append(score.Name).Append("\" (a score)").Append(Said(score.Instructions)).Append(" Levels, lowest first:\n");
                for (var i = 0; i < score.Levels.Count; i++)
                    prompt.Append("  - \"").Append(Position(i)).Append("\": ").Append(score.Levels[i]).Append('\n');
                break;
            case YesNoQuestionSpec yesNo:
                prompt.Append("- \"").Append(yesNo.Name).Append("\" (a yes-no): ").Append(yesNo.Statement).Append('\n');
                break;
        }
    }

    private static string Said(string instructions) => string.IsNullOrWhiteSpace(instructions) ? "." : ": " + instructions.Trim();

    private static string Position(int index) => index.ToString(CultureInfo.InvariantCulture);

    private static int IndexOf(IReadOnlyList<string> options, string key)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (options[i] == key)
                return i;
        }

        return int.MaxValue;
    }

    // A provider that took the schema in its prompt may fence its JSON in markdown: the fence is not the answer.
    private static string Unfenced(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;
        var firstLine = trimmed.IndexOf('\n', StringComparison.Ordinal);
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstLine < 0 || lastFence <= firstLine ? trimmed : trimmed[(firstLine + 1)..lastFence].Trim();
    }
}
