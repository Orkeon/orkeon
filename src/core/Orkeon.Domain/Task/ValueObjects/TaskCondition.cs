using System.Collections.Immutable;

namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>How a threshold compares.</summary>
public enum ConditionComparison
{
    /// <summary>At least.</summary>
    GreaterOrEqual,

    /// <summary>More than.</summary>
    Greater,

    /// <summary>At most.</summary>
    LessOrEqual,

    /// <summary>Less than.</summary>
    Less,
}

/// <summary>One clause of a condition.</summary>
public sealed class ConditionClause
{
    private ConditionClause(string question, IReadOnlyList<string> options, string? option, ConditionComparison comparison, double value, bool isThreshold)
    {
        Question = question;
        Options = options;
        Option = option;
        Comparison = comparison;
        Value = value;
        IsThreshold = isThreshold;
    }

    /// <summary>Gets the question name.</summary>
    public string Question { get; }

    /// <summary>Gets the listed options of an option clause.</summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>Gets the option a threshold reads.</summary>
    public string? Option { get; }

    /// <summary>Gets the comparison of a threshold.</summary>
    public ConditionComparison Comparison { get; }

    /// <summary>Gets the value of a threshold.</summary>
    public double Value { get; }

    private bool IsThreshold { get; }

    /// <summary>Creates an option clause.</summary>
    public static ConditionClause OneOf(TaskDecision decision, string question, IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(options);
        var choice = decision.Question(question) as ChoiceQuestionSpec
            ?? throw new ArgumentException($"'{question}' is not a choice question of the decision.", nameof(question));
        if (options.Count == 0 || options.Any(o => !choice.Options.ContainsKey(o)))
            throw new ArgumentException($"The listed options are not all options of question '{question}'.", nameof(options));
        return new ConditionClause(question, options.ToImmutableList(), null, default, 0, false);
    }

    /// <summary>Creates a threshold clause.</summary>
    public static ConditionClause Threshold(TaskDecision decision, string question, string? option, ConditionComparison comparison, double value)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var spec = decision.Question(question)
            ?? throw new ArgumentException($"Unknown question '{question}'.", nameof(question));
        if (spec is ChoiceQuestionSpec choice)
        {
            if (option is null || !choice.Options.ContainsKey(option))
                throw new ArgumentException($"Question '{question}' has no option '{option}'.", nameof(option));
        }
        else if (option is not null)
        {
            throw new ArgumentException($"Question '{question}' has no options.", nameof(option));
        }

        // A probability lies in [0, 1]; a score is the weighted position on the levels, from 0 to the
        // last level (BR-03), the scale the model answers on and the fallback shares.
        var top = spec is ScoreQuestionSpec score ? score.Levels.Count - 1 : 1;
        if (!double.IsFinite(value) || value < 0 || value > top)
            throw new ArgumentOutOfRangeException(nameof(value), spec is ScoreQuestionSpec
                ? $"A threshold on the score '{question}' lies in [0, {top}], the positions of its levels."
                : $"A threshold on '{question}' is a probability in [0, 1].");

        return new ConditionClause(question, [], option, comparison, value, true);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is ConditionClause other
        && Question == other.Question
        && Option == other.Option
        && Comparison == other.Comparison
        && Value == other.Value
        && IsThreshold == other.IsThreshold
        && Options.SequenceEqual(other.Options);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Question, Option, Comparison, Value, IsThreshold, ContentEquality.Hash(Options));

    internal bool Holds(DecisionOutcome outcome, TaskDecision decision)
    {
        if (!outcome.Answers.TryGetValue(Question, out var answer))
            return false;

        if (!IsThreshold)
            return answer.Chosen is not null && Options.Contains(answer.Chosen) && answer.Confidence >= decision.ConfidenceFloor;

        var measured = Option is not null
            ? answer.Probabilities.TryGetValue(Option, out var p) ? p : (double?)null
            : answer.Score ?? answer.Probability;
        return measured is { } m && Comparison switch
        {
            ConditionComparison.GreaterOrEqual => m >= Value,
            ConditionComparison.Greater => m > Value,
            ConditionComparison.LessOrEqual => m <= Value,
            _ => m < Value,
        };
    }
}

/// <summary>When a task runs, from a decision task's outcome.</summary>
public sealed class TaskCondition
{
    private TaskCondition(string decisionTask, IReadOnlyList<ConditionClause> clauses, bool isOtherwise)
    {
        DecisionTask = decisionTask;
        Clauses = clauses;
        IsOtherwise = isOtherwise;
    }

    /// <summary>Gets the decision task name.</summary>
    public string DecisionTask { get; }

    /// <summary>Gets the clauses.</summary>
    public IReadOnlyList<ConditionClause> Clauses { get; }

    /// <summary>Gets whether this is the otherwise branch.</summary>
    public bool IsOtherwise { get; }

    /// <summary>Creates a condition.</summary>
    public static TaskCondition Create(string decisionTask, IReadOnlyList<ConditionClause> clauses)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionTask);
        ArgumentNullException.ThrowIfNull(clauses);
        if (clauses.Count == 0)
            throw new ArgumentException("A condition has at least one clause.", nameof(clauses));
        return new TaskCondition(decisionTask, clauses.ToImmutableList(), false);
    }

    /// <summary>Creates the otherwise condition.</summary>
    public static TaskCondition Otherwise(string decisionTask)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionTask);
        return new TaskCondition(decisionTask, [], true);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is TaskCondition other
        && DecisionTask == other.DecisionTask
        && IsOtherwise == other.IsOtherwise
        && Clauses.SequenceEqual(other.Clauses);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(DecisionTask, IsOtherwise, ContentEquality.Hash(Clauses));

    /// <summary>Tells whether every clause holds.</summary>
    public bool Holds(DecisionOutcome outcome, TaskDecision decision)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(decision);
        return Clauses.All(c => c.Holds(outcome, decision));
    }

    /// <summary>Tells whether no sibling holds.</summary>
    public bool OtherwiseHolds(DecisionOutcome outcome, TaskDecision decision, IReadOnlyList<TaskCondition> siblings)
    {
        ArgumentNullException.ThrowIfNull(siblings);
        return IsOtherwise && !siblings.Any(s => s.Holds(outcome, decision));
    }
}
