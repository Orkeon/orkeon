using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Constants.Protocol;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// Answers a decision task in one request: by the decision model with the circuit on; with it off, by the
/// task's <c>without:</c> relay agent in an <c>optional</c> crew (#430), else by its fallback.
/// </summary>
public sealed class DecisionTaskRunner
{
    private readonly IDecisionModel? _model;
    private readonly IDecisionRelay? _relay;
    private readonly DecisionCircuit _circuit;
    private static readonly Action<ILogger, string, Exception?> LogFailed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, "DecisionTaskFailed"), "Decision task failed: {Reason}");
    private static readonly Action<ILogger, string, string, Exception?> LogRelayRefused =
        LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(2, "DecisionRelayAnswerRefused"), "The relay answer of decision task {Task} was refused and is asked again: {Reason}");

    private readonly ILogger<DecisionTaskRunner> _logger;

    /// <summary>Initializes a new instance of <see cref="DecisionTaskRunner"/>.</summary>
    /// <param name="model">The decision model, null when none is configured.</param>
    /// <param name="circuit">Whether the model is asked.</param>
    /// <param name="logger">The logger.</param>
    public DecisionTaskRunner(IDecisionModel? model, DecisionCircuit circuit, ILogger<DecisionTaskRunner> logger)
        : this(model, null, circuit, logger)
    {
    }

    /// <summary>Initializes a new instance of <see cref="DecisionTaskRunner"/> with a relay.</summary>
    /// <param name="model">The decision model, null when none is configured.</param>
    /// <param name="relay">The relay that asks a <c>without:</c> agent, null when the host has none.</param>
    /// <param name="circuit">Whether the model is asked.</param>
    /// <param name="logger">The logger.</param>
    public DecisionTaskRunner(IDecisionModel? model, IDecisionRelay? relay, DecisionCircuit circuit, ILogger<DecisionTaskRunner> logger)
    {
        _model = model;
        _relay = relay;
        _circuit = circuit;
        _logger = logger;
    }

    /// <summary>
    /// Gets the clock the request windows of a relay call count on (#430): the host's, as the agent loops'.
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Gets the host's per-agent cap a relay call waits under (<c>RateLimiting:AgentRequestsPerMinute</c>), beside
    /// the relay agent's own <c>maxRpm</c> and the crew's; null sets none.
    /// </summary>
    public int? AgentRequestsPerMinute { get; init; }

    /// <summary>Gets a value indicating whether the decision model is asked, read at call time.</summary>
    public bool IsEnabled => Circuit.Enabled;

    /// <summary>
    /// Gets the circuit this runner applies, read at call time as <see cref="DecisionCircuit.Of(IServiceProvider, DecisionCircuit?)"/>
    /// folds it: the one it was given when it is off, then the run in progress when it is off
    /// (<see cref="DecisionCircuitScope.Current"/>, so a runner built once per host follows each run), then off by
    /// <see cref="DecisionCircuitCause.NoProvider"/> when no decision model is configured; on otherwise.
    /// </summary>
    public DecisionCircuit Circuit
    {
        get
        {
            if (!_circuit.Enabled)
                return _circuit;
            if (DecisionCircuitScope.Current is { Enabled: false } running)
                return running;
            return _model is null ? DecisionCircuit.OffBy(DecisionCircuitCause.NoProvider) : _circuit;
        }
    }

    /// <summary>
    /// What answers <paramref name="task"/> under this runner's circuit: the decision model with it on; with it
    /// off, the task's <c>without:</c> relay when it declares one, else its fallback.
    /// </summary>
    /// <param name="task">The decision task.</param>
    /// <returns>One of <see cref="DecisionSources"/>.</returns>
    public string SourceOf(CrewTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (IsEnabled)
            return DecisionSources.Model;
        return task.Decision?.Without is not null ? DecisionSources.Relay : DecisionSources.Fallback;
    }

    /// <summary>Runs a decision task.</summary>
    /// <param name="task">The decision task.</param>
    /// <param name="inputs">The outputs of the tasks it depends on, labelled by task name.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The task result.</returns>
    public System.Threading.Tasks.Task<TaskResult> RunAsync(
        CrewTask task,
        IReadOnlyList<(string TaskName, string Output)> inputs,
        CancellationToken ct) =>
        RunAsync(task, inputs, null, ct);

    /// <summary>Runs a decision task, with the crew agent its <c>without:</c> relay names.</summary>
    /// <param name="task">The decision task.</param>
    /// <param name="inputs">The outputs of the tasks it depends on, labelled by task name.</param>
    /// <param name="relayAgent">The agent of the crew the relay names; null when the task has no relay or the crew has no such agent.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The task result; <see cref="TaskResult.DecisionSource"/> says what answered it.</returns>
    public async System.Threading.Tasks.Task<TaskResult> RunAsync(
        CrewTask task,
        IReadOnlyList<(string TaskName, string Output)> inputs,
        Orkeon.Domain.Agent.Agent? relayAgent,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(inputs);
        var decision = task.Decision ?? throw new ArgumentException("The task is not a decision task.", nameof(task));
        var started = Stopwatch.GetTimestamp();

        // The circuit is the run's, read now: off, an optional crew's decision task is answered by its without:
        // relay (#430), whatever turned the circuit off; a crew with no mode keeps its fallback. Neither is a
        // second chance after a failure: a model or a relay that fails fails the task.
        var enabled = IsEnabled;
        if (!enabled && decision.Without is { } relay)
            return await RelayAsync(task, decision, relay, relayAgent, inputs, started, ct).ConfigureAwait(false);

        if (!enabled)
            return Succeeded(decision.FallbackOutcome(), started, DecisionSources.Fallback);

        try
        {
            var response = await _model!.DecideAsync(BuildRequest(decision, inputs), ct).ConfigureAwait(false);
            return Succeeded(ToOutcome(decision, response), started, DecisionSources.Model, response.Usage.InputTokens ?? 0, response.Usage.OutputTokens ?? 0);
        }
        catch (DecisionModelException ex)
        {
            // Cancellation is not caught: the strategies record an interruption, never a failed task.
            var reason = ex.InnerException is null ? ex.Message : $"{ex.Message}: {ex.InnerException.Message}";
            return Failed(reason, ex, started, DecisionSources.Model);
        }
    }

    /// <summary>
    /// The relay (#430): one LLM call to the relay agent, its answer checked and derived like the model's. An
    /// answer that fails a check is asked once more, with the reason; then the task fails with it.
    /// </summary>
    private async System.Threading.Tasks.Task<TaskResult> RelayAsync(
        CrewTask task, TaskDecision decision, DecisionRelay relay, Orkeon.Domain.Agent.Agent? agent,
        IReadOnlyList<(string TaskName, string Output)> inputs, long started, CancellationToken ct)
    {
        var name = task.Key ?? task.Description.Value;
        if (_relay is null || agent is null)
        {
            var missing = _relay is null
                ? "this host registers no decision relay"
                : $"the crew has no agent '{relay.Agent}'";
            return Failed($"Decision task '{name}' is answered by its without: relay (agent '{relay.Agent}') when {Circuit.OffReason}, and {missing}.", null, started, DecisionSources.Relay);
        }

        var state = BuildRequest(decision, inputs).State;
        int inputTokens = 0, outputTokens = 0;
        string? refusal = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            DecisionRelayResponse answer;
            // A relay call is a model call of the relay agent: it waits its turn in the agent's window and the
            // crew's, as an agent loop's call does, and says so when it waited (#430).
            await LlmCallGate.WaitTurnAsync(agent, AgentRequestsPerMinute, TimeProvider, _logger, null, ct).ConfigureAwait(false);
            try
            {
                answer = await _relay.AskAsync(
                    DecisionRelayProtocol.Request(agent, task.Id.ToString(), decision, relay, state, refusal), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Failed($"The relay agent '{relay.Agent}' of decision task '{name}' failed: {ex.Message}", ex, started, DecisionSources.Relay, inputTokens, outputTokens);
            }

            inputTokens += answer.InputTokens;
            outputTokens += answer.OutputTokens;
            try
            {
                var response = DecisionRelayProtocol.Parse(answer.Text, decision, DecisionRelayProtocol.ModelOf(agent));
                return Succeeded(ToOutcome(decision, response), started, DecisionSources.Relay, inputTokens, outputTokens);
            }
            catch (FormatException ex)
            {
                refusal = ex.Message;
                if (attempt == 1)
                    LogRelayRefused(_logger, name, refusal, null);
            }
        }

        return Failed($"The relay agent '{relay.Agent}' of decision task '{name}' answered twice with an answer that fails its checks: {refusal}", null, started, DecisionSources.Relay, inputTokens, outputTokens);
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

    private TaskResult Failed(string reason, Exception? ex, long started, string source, int inputTokens = 0, int outputTokens = 0)
    {
        LogFailed(_logger, reason, ex);
        return new TaskResult(false, string.Empty, null, [], Stopwatch.GetElapsedTime(started), reason, inputTokens + outputTokens)
        {
            DecisionSource = source,
            PromptTokens = inputTokens,
            CompletionTokens = outputTokens,
        };
    }

    private static TaskResult Succeeded(DecisionOutcome outcome, long started, string source, int inputTokens = 0, int outputTokens = 0) =>
        new(true, outcome.ToCompactJson(), null, [], Stopwatch.GetElapsedTime(started), null, inputTokens + outputTokens)
        {
            Decision = outcome,
            DecisionSource = source,
            PromptTokens = inputTokens,
            CompletionTokens = outputTokens,
        };
}
