using System.Globalization;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Scripting.Builders;

/// <summary>
/// Maps the plain data of <c>.decision({...})</c> and <c>.when(...)</c> onto the domain's
/// <see cref="TaskDecision"/> and <see cref="TaskCondition"/>; every rule lives in their Create methods.
/// </summary>
internal static class JsDecisionMapper
{
    /// <summary>Builds the decision of task <paramref name="task"/>; a refusal names the task and the part.</summary>
    internal static TaskDecision MapDecision(string task, object spec)
    {
        var map = AsMap(spec) ?? throw new InvalidOperationException($"Task '{task}' decision: expects an object.");
        var questions = new List<DecisionQuestionSpec>();
        if (map.TryGetValue("questions", out var raw) && AsMap(raw) is { } asked)
        {
            foreach (var (name, question) in asked)
            {
                try
                {
                    questions.Add(MapQuestion(name, AsMap(question) ?? throw new ArgumentException("A question is an object.")));
                }
                catch (ArgumentException ex)
                {
                    throw new InvalidOperationException($"Task '{task}' decision.questions.{name}: {ex.Message}", ex);
                }
            }
        }

        try
        {
            return TaskDecision.Create(
                questions,
                map.TryGetValue("minConfidence", out var floor) && floor is not null
                    ? IsNumber(floor)
                        ? Convert.ToDouble(floor, CultureInfo.InvariantCulture)
                        : throw new ArgumentException("minConfidence must be a number.")
                    : null,
                map.TryGetValue("state", out var state) ? Strings(state) : null,
                map.TryGetValue("longContext", out var longContext) && longContext is not null
                    ? longContext is bool flag ? flag : throw new ArgumentException("longContext must be a bool.")
                    : false,
                map.TryGetValue("fallback", out var fallback) && AsMap(fallback) is { } answers
                    ? answers.ToDictionary(a => a.Key, a => Convert.ToString(a.Value, CultureInfo.InvariantCulture)!)
                    : null);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidCastException or OverflowException)
        {
            var part = (ex as ArgumentException)?.ParamName == "fallback" ? "decision.fallback" : "decision";
            throw new InvalidOperationException($"Task '{task}' {part}: {ex.Message}", ex);
        }
    }

    /// <summary>Builds the condition of task <paramref name="task"/> against the decision it reads.</summary>
    internal static TaskCondition MapCondition(string task, string decisionTask, object spec, TaskDecision decision)
    {
        try
        {
            if (spec is string text)
            {
                return text == "otherwise"
                    ? TaskCondition.Otherwise(decisionTask)
                    : throw new ArgumentException($"'{text}' is not a clause object nor otherwise.");
            }

            var clauses = new List<ConditionClause>();
            foreach (var (key, value) in AsMap(spec) ?? throw new ArgumentException("The clauses are an object."))
            {
                var parts = key.Split('.');
                if (parts.Length > 2)
                    throw new ArgumentException($"'{key}' has more than two segments (question.option).");
                clauses.Add(value switch
                {
                    string threshold => ParseThreshold(decision, parts[0], parts.Length > 1 ? parts[1] : null, threshold),
                    IEnumerable<object?> when parts.Length > 1 =>
                        throw new ArgumentException($"clause '{key}' names an option, which takes a threshold string, not a list."),
                    IEnumerable<object?> list when list.All(o => o is string) =>
                        ConditionClause.OneOf(decision, parts[0], list.Select(o => (string)o!).ToList()),
                    _ => throw new ArgumentException($"clause '{key}' must be a string or a list of strings."),
                });
            }

            return TaskCondition.Create(decisionTask, clauses);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            throw new InvalidOperationException($"Task '{task}' when on '{decisionTask}': {ex.Message}", ex);
        }
    }

    private static readonly string[] QuestionKinds = ["choice", "score", "noul"];

    private static DecisionQuestionSpec MapQuestion(string name, IDictionary<string, object?> question)
    {
        var kinds = QuestionKinds.Where(question.ContainsKey).ToList();
        if (kinds.Count > 1)
            throw new ArgumentException($"declares more than one kind ({string.Join(", ", kinds)}); a question has exactly one.");
        var instructions = question.TryGetValue("instructions", out var i) ? i?.ToString() ?? string.Empty : string.Empty;
        if (question.TryGetValue("choice", out var choice) && AsMap(choice) is { } options)
            return ChoiceQuestionSpec.Create(name, instructions, options.ToDictionary(o => o.Key, o => o.Value?.ToString() ?? string.Empty));
        if (question.TryGetValue("score", out var score) && Strings(score) is { } levels)
            return ScoreQuestionSpec.Create(name, instructions, levels);
        return YesNoQuestionSpec.Create(name, question.TryGetValue("noul", out var statement) ? statement?.ToString() ?? string.Empty : string.Empty);
    }

    private static ConditionClause ParseThreshold(TaskDecision decision, string question, string? option, string text) =>
        ThresholdExpression.TryParse(text, out var comparison, out var number)
            ? ConditionClause.Threshold(decision, question, option, comparison, number)
            : throw new ArgumentException($"'{text}' is not a threshold such as '>= 0.7'.");

    private static bool IsNumber(object value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static IDictionary<string, object?>? AsMap(object? value) => value as IDictionary<string, object?>;

    private static List<string>? Strings(object? value) =>
        value is IEnumerable<object?> items ? items.Select(o => o?.ToString() ?? string.Empty).ToList() : null;
}
