using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Crew.Execution;

/// <summary>Answers a decision task with the decision model in one request, or by its fallback.</summary>
public sealed class DecisionTaskRunner
{
    private readonly IDecisionModel? _model;
    private readonly DecisionCircuit _circuit;
    private static readonly Action<ILogger, string, Exception?> LogFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, "DecisionTaskFailed"), "Decision task failed: {Reason}");

    private readonly ILogger<DecisionTaskRunner> _logger;

    /// <summary>Initializes a new instance of <see cref="DecisionTaskRunner"/>.</summary>
    /// <param name="model">The decision model, null when none is configured.</param>
    /// <param name="circuit">Whether the model is asked.</param>
    /// <param name="logger">The logger.</param>
    public DecisionTaskRunner(IDecisionModel? model, DecisionCircuit circuit, ILogger<DecisionTaskRunner> logger)
    {
        _model = model;
        _circuit = circuit;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether the decision model is asked.</summary>
    public bool IsEnabled => _circuit.Enabled && _model is not null;

    /// <summary>Runs a decision task.</summary>
    /// <param name="task">The decision task.</param>
    /// <param name="inputs">The outputs of the tasks it depends on, labelled by task name.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The task result.</returns>
    public async System.Threading.Tasks.Task<TaskResult> RunAsync(
        CrewTask task,
        IReadOnlyList<(string TaskName, string Output)> inputs,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(inputs);
        var decision = task.Decision ?? throw new ArgumentException("The task is not a decision task.", nameof(task));
        var started = Stopwatch.GetTimestamp();

        if (!IsEnabled)
            return Succeeded(decision.FallbackOutcome(), started);

        try
        {
            var response = await _model!.DecideAsync(BuildRequest(decision, inputs), ct).ConfigureAwait(false);
            return Succeeded(ToOutcome(decision, response), started, response.Usage.InputTokens ?? 0, response.Usage.OutputTokens ?? 0);
        }
        catch (DecisionModelException ex)
        {
            // Cancellation is not caught: the strategies record an interruption, never a failed task.
            var reason = ex.InnerException is null ? ex.Message : $"{ex.Message}: {ex.InnerException.Message}";
            LogFailed(_logger, reason, ex);
            return new TaskResult(false, string.Empty, null, [], Stopwatch.GetElapsedTime(started), reason);
        }
    }

    private static DecisionRequest BuildRequest(TaskDecision decision, IReadOnlyList<(string TaskName, string Output)> inputs)
    {
        var state = new JsonArray();
        foreach (var (name, output) in inputs)
        {
            if (decision.StateFrom.Count == 0 || decision.StateFrom.Contains(name))
                state.Add(new JsonObject { ["task"] = name, ["output"] = output });
        }

        return new DecisionRequest
        {
            State = state,
            Questions = decision.Questions.ToImmutableDictionary(q => q.Name, ToQuestion),
            LongContext = decision.LongContext,
        };
    }

    private static DecisionQuestion ToQuestion(DecisionQuestionSpec spec) => spec switch
    {
        ChoiceQuestionSpec choice => new ChoiceQuestion
        {
            Instructions = JsonValue.Create(choice.Instructions),
            Criteria = choice.Options.ToImmutableDictionary(o => o.Key, o => (JsonNode?)JsonValue.Create(o.Value)),
        },
        ScoreQuestionSpec score => new ScoreQuestion
        {
            Instructions = JsonValue.Create(score.Instructions),
            Criteria = score.Levels.Select(l => (JsonNode)JsonValue.Create(l)!).ToImmutableList(),
        },
        YesNoQuestionSpec yesNo => new NoulQuestion { Instructions = JsonValue.Create(yesNo.Statement) },
        _ => throw new NotSupportedException($"Unsupported decision question '{spec.Name}'."),
    };

    private static DecisionOutcome ToOutcome(TaskDecision decision, DecisionResponse response)
    {
        // A question the model left unanswered, or answered with another kind, fails the task
        // (BR-06): an empty answer would let the otherwise task run on nothing.
        var answers = new Dictionary<string, DecisionAnswerValue>();
        foreach (var question in decision.Questions)
        {
            if (!response.Answers.TryGetValue(question.Name, out var answer))
                throw new DecisionModelException($"The decision model answered no question '{question.Name}'.", null, isTransient: false);
            answers[question.Name] = ToValue(question, answer);
        }

        return new DecisionOutcome { Answers = answers };
    }

    private static DecisionAnswerValue ToValue(DecisionQuestionSpec question, DecisionAnswer answer) => (question, answer) switch
    {
        (ChoiceQuestionSpec choice, ChoiceAnswer c) when choice.Options.ContainsKey(c.Choice) =>
            new DecisionAnswerValue { Chosen = c.Choice, Confidence = c.Confidence, Probabilities = c.Probabilities },
        (ChoiceQuestionSpec, ChoiceAnswer c) => throw new DecisionModelException(
            $"The decision model answered question '{question.Name}' with '{c.Choice}', which is not one of its options.", null, isTransient: false),
        (ScoreQuestionSpec, ScoreAnswer s) => new DecisionAnswerValue { Score = s.Score, Confidence = s.Confidence, Probabilities = s.Probabilities },
        (YesNoQuestionSpec, NoulAnswer n) => new DecisionAnswerValue
        {
            Chosen = n.Noul >= 0.5 ? "yes" : "no",
            Probability = n.Noul,
            Confidence = Math.Max(n.Noul, 1 - n.Noul),
        },
        _ => throw new DecisionModelException(
            $"The decision model answered question '{question.Name}' with a {KindOf(answer)} answer, not a {KindOf(question)} one.", null, isTransient: false),
    };

    private static string KindOf(DecisionAnswer answer) => answer switch
    {
        ChoiceAnswer => "choice",
        ScoreAnswer => "score",
        NoulAnswer => "noul",
        BoxAnswer => "box",
        _ => answer.GetType().Name,
    };

    private static string KindOf(DecisionQuestionSpec question) => question switch
    {
        ChoiceQuestionSpec => "choice",
        ScoreQuestionSpec => "score",
        _ => "noul",
    };

    private static TaskResult Succeeded(DecisionOutcome outcome, long started, int inputTokens = 0, int outputTokens = 0) =>
        new(true, outcome.ToCompactJson(), null, [], Stopwatch.GetElapsedTime(started), null, inputTokens + outputTokens)
        {
            Decision = outcome,
            PromptTokens = inputTokens,
            CompletionTokens = outputTokens,
        };
}
