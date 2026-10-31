using System.Collections.Immutable;
using Orkeon.Constants.Llm;

namespace Orkeon.Domain.Task.ValueObjects;

/// <summary>The decision a task makes instead of an agent.</summary>
public sealed class TaskDecision
{
    private TaskDecision(
        IReadOnlyList<DecisionQuestionSpec> questions,
        double confidenceFloor,
        IReadOnlyList<string> stateFrom,
        bool longContext,
        IReadOnlyDictionary<string, string> fallback)
    {
        Questions = questions;
        ConfidenceFloor = confidenceFloor;
        StateFrom = stateFrom;
        LongContext = longContext;
        Fallback = fallback;
    }

    /// <summary>Gets the questions.</summary>
    public IReadOnlyList<DecisionQuestionSpec> Questions { get; }

    /// <summary>Gets the confidence floor.</summary>
    public double ConfidenceFloor { get; }

    /// <summary>Gets the tasks whose output feeds the decision.</summary>
    public IReadOnlyList<string> StateFrom { get; }

    /// <summary>Gets whether the long-context model is used.</summary>
    public bool LongContext { get; }

    /// <summary>Gets the fallback answers, question to option.</summary>
    public IReadOnlyDictionary<string, string> Fallback { get; }

    /// <summary>Gets whether an explicit fallback is declared.</summary>
    public bool HasExplicitFallback => Fallback.Count > 0;

    /// <summary>Creates a decision.</summary>
    public static TaskDecision Create(
        IReadOnlyList<DecisionQuestionSpec> questions,
        double? confidenceFloor = null,
        IReadOnlyList<string>? stateFrom = null,
        bool longContext = false,
        IReadOnlyDictionary<string, string>? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
            throw new ArgumentException("A decision asks at least one question.", nameof(questions));
        foreach (var question in questions)
            question.Validate();
        if (questions.Select(q => q.Name).Distinct(StringComparer.Ordinal).Count() != questions.Count)
            throw new ArgumentException("Question names must be unique.", nameof(questions));
        if (questions.Any(q => q.Name == DecisionOutcome.DefaultedKey))
            throw new ArgumentException($"A question cannot be named '{DecisionOutcome.DefaultedKey}': the decision output reserves that key.", nameof(questions));

        var floor = confidenceFloor ?? DecisionLimits.DefaultConfidenceFloor;
        if (!double.IsFinite(floor) || floor is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(confidenceFloor), "The confidence floor must be a finite number in [0, 1].");

        var answers = (fallback ?? ImmutableDictionary<string, string>.Empty).ToImmutableDictionary();
        foreach (var (question, answer) in answers)
        {
            var spec = questions.FirstOrDefault(q => q.Name == question)
                ?? throw new ArgumentException($"The fallback names the unknown question '{question}'.", nameof(fallback));
            if (!Answers(spec).Contains(answer, StringComparer.Ordinal))
                throw new ArgumentException($"The fallback answer '{answer}' is not an answer of question '{question}'.", nameof(fallback));
        }

        return new TaskDecision(questions.Select(Frozen).ToImmutableList(), floor, (stateFrom ?? []).ToImmutableList(), longContext, answers);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is TaskDecision other
        && ConfidenceFloor.Equals(other.ConfidenceFloor)
        && LongContext == other.LongContext
        && Questions.SequenceEqual(other.Questions)
        && StateFrom.SequenceEqual(other.StateFrom)
        && ContentEquality.Same(Fallback, other.Fallback);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(ConfidenceFloor, LongContext, ContentEquality.Hash(Questions), ContentEquality.Hash(StateFrom), ContentEquality.Hash(Fallback));

    /// <summary>Gets a question by name.</summary>
    public DecisionQuestionSpec? Question(string name) => Questions.FirstOrDefault(q => q.Name == name);

    /// <summary>Gets the outcome the fallback gives.</summary>
    public DecisionOutcome FallbackOutcome() =>
        DecisionOutcome.Defaulted(Questions.ToDictionary(q => q.Name, AnswerOf));

    private DecisionAnswerValue AnswerOf(DecisionQuestionSpec question)
    {
        if (!Fallback.TryGetValue(question.Name, out var chosen))
            return new DecisionAnswerValue();

        var answer = new DecisionAnswerValue
        {
            Chosen = chosen,
            Confidence = 1,
            Probabilities = Answers(question).ToDictionary(a => a, a => a == chosen ? 1.0 : 0.0),
        };
        return question switch
        {
            // A score answer is keyed by position and has no chosen option, as the model's is (BR-03).
            ScoreQuestionSpec score => new DecisionAnswerValue
            {
                Score = score.Levels.ToList().IndexOf(chosen),
                Confidence = 1,
                Probabilities = score.Levels.Select((level, i) => (i, level))
                    .ToDictionary(p => p.i.ToString(System.Globalization.CultureInfo.InvariantCulture), p => p.level == chosen ? 1.0 : 0.0),
            },
            YesNoQuestionSpec => answer with { Probability = chosen == "yes" ? 1 : 0 },
            _ => answer,
        };
    }

    private static DecisionQuestionSpec Frozen(DecisionQuestionSpec question) => question switch
    {
        ChoiceQuestionSpec choice => choice with { Options = choice.Options.ToImmutableDictionary() },
        ScoreQuestionSpec score => score with { Levels = score.Levels.ToImmutableList() },
        _ => question,
    };

    private static IEnumerable<string> Answers(DecisionQuestionSpec question) => question switch
    {
        ChoiceQuestionSpec choice => choice.Options.Keys,
        ScoreQuestionSpec score => score.Levels,
        _ => ["yes", "no"],
    };
}
