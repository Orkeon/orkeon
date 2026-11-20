using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Scripting.Cli.Commands.Forge;

/// <summary>
/// The forge's own decision circuit (#435): whether the forge asks System One for advice while it designs a crew.
/// Resolved once per process, never stored with the session, and never a <see cref="DecisionCircuit"/>: the trial
/// of the crew follows the engine host's circuit and the crew's <c>--crew-decision</c> (#431), whatever this one
/// says. Every answer is advice: it never moves the cycle, the blueprint, the judge's verdict or the arbitration.
/// </summary>
internal sealed class ForgeSystemOne
{
    /// <summary>The warning a failed call emits; the stage carries on as if the circuit were off for that call.</summary>
    public const string FailedWarning = "FORGE-SYSTEMONE-FAILED";

    /// <summary>The event kind of one answered call.</summary>
    public const string AnsweredKind = "systemone.answered";

    /// <summary>The use of the process suggestion, asked by the blueprint stage.</summary>
    public const string UseProcess = "process";

    /// <summary>The use of the brief's coverage, asked by the brief stage.</summary>
    public const string UseCoverage = "coverage";

    /// <summary>The use of the trial's estimate, asked by the test stage.</summary>
    public const string UseEstimate = "estimate";

    /// <summary>The use of the per-criterion reading of the output, asked by the diagnosis.</summary>
    public const string UseCriteria = "criteria";

    /// <summary>The probability a process suggestion needs to reach the assistant.</summary>
    public const double ProcessHintThreshold = 0.6;

    /// <summary>The levels of the coverage question, lowest first.</summary>
    public static readonly IReadOnlyList<string> CoverageLevels = ["misses", "partial", "covers"];

    private static readonly JsonSerializerOptions StateOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IDecisionModel? _model;

    private ForgeSystemOne(IDecisionModel? model, string? cause)
    {
        _model = model;
        Cause = cause;
    }

    /// <summary>The circuit of a forge that asks nothing, for the stages built without one.</summary>
    public static ForgeSystemOne None { get; } = new(null, null);

    /// <summary>Whether the forge asks System One.</summary>
    public bool Enabled => _model is not null;

    /// <summary>Why the forge asks nothing (<c>switch</c>, <c>settings</c>, <c>no-provider</c>); null when on.</summary>
    public string? Cause { get; }

    /// <summary>The spelling of the circuit in <c>session.started</c>: <c>on</c> or <c>off</c>.</summary>
    public string CircuitName => Enabled ? "on" : "off";

    /// <summary>An on circuit over <paramref name="model"/>.</summary>
    public static ForgeSystemOne On(IDecisionModel model) =>
        new(model ?? throw new ArgumentNullException(nameof(model)), null);

    /// <summary>An off circuit, by <paramref name="cause"/>.</summary>
    public static ForgeSystemOne Off(string cause) => new(null, cause);

    /// <summary>
    /// The forge's circuit in <paramref name="services"/>: off by <c>switch</c> under <c>--no-decision</c>, which
    /// resolves nothing; else off by the engine host's own cause (<c>Orkeon:Decision:Enabled=false</c>, no decision
    /// model); else on. The host's circuit is only read, never changed: the trial still follows it.
    /// </summary>
    public static ForgeSystemOne Resolve(bool noDecision, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (noDecision)
            return Off(ForgeCrewDecision.CauseName(DecisionCircuit.Off)!);

        // DecisionCircuit.Of is on only when a decision model is registered, so the model resolves here.
        var host = DecisionCircuit.Of(services);
        return host.Enabled
            ? On(services.GetRequiredService<IDecisionModel>())
            : Off(ForgeCrewDecision.CauseName(host) ?? ForgeCrewDecision.CauseName(DecisionCircuit.OffBy(DecisionCircuitCause.Settings))!);
    }

    /// <summary>
    /// The processes the suggestion chooses among: those the crew's decision mode lets the plan hold. A crew that
    /// decides (<c>optional</c>, <c>required</c>) needs a process that runs decision tasks.
    /// </summary>
    public static IReadOnlyList<string> ProcessesFor(string? crewDecision) =>
        ForgeCrewDecision.Normalize(crewDecision) == ForgeCrewDecision.None
            ? ["sequential", "parallel", "graph", "hierarchical", "consensual", "autonomous"]
            : ["sequential", "parallel", "graph"];

    private static string ProcessMeaning(string process) => process switch
    {
        "sequential" => "the tasks run one after the other, each reading what the earlier ones wrote",
        "parallel" => "independent tasks run at the same time",
        "graph" => "the tasks form a graph whose edges, some conditional, say what runs next",
        "hierarchical" => "a manager agent plans the work and delegates it to the other agents",
        "consensual" => "several agents work on the same question and agree on one answer",
        _ => "the agents organise the work among themselves, with no fixed plan",
    };

    /// <summary>
    /// The blueprint stage's question: which process fits <paramref name="brief"/>, among those
    /// <paramref name="crewDecision"/> allows. The suggestion comes back only when its probability reaches
    /// <see cref="ProcessHintThreshold"/>; null otherwise, or when the circuit is off or the call failed.
    /// </summary>
    public async Task<(ForgeProcessHint? Hint, ForgeUsageSnapshot Usage)> SuggestProcessAsync(
        ForgeEventWriter events, ForgeBrief brief, string? crewDecision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);
        if (!Enabled)
            return (null, default);

        var processes = ProcessesFor(crewDecision);
        var question = new ChoiceQuestion
        {
            Instructions = JsonValue.Create("Which orchestration process fits the team this brief describes?"),
            Criteria = processes.ToImmutableDictionary(static p => p, static p => (JsonNode?)JsonValue.Create(ProcessMeaning(p))),
        };
        var response = await AskAsync(events, ForgeEventWriter.Spell(ForgeState.Blueprint), UseProcess,
            StateOf(new { brief }), [KeyValuePair.Create<string, DecisionQuestion>(UseProcess, question)], cancellationToken)
            .ConfigureAwait(false);
        if (response.Answer is null)
            return (null, response.Usage);

        if (!response.Answer.Answers.TryGetValue(UseProcess, out var answer) || answer is not ChoiceAnswer choice
            || !processes.Contains(choice.Choice, StringComparer.Ordinal))
        {
            return (null, response.Usage);
        }

        var p = choice.Probabilities.TryGetValue(choice.Choice, out var probability) ? probability : choice.Confidence;
        return (p >= ProcessHintThreshold ? new ForgeProcessHint(choice.Choice, p) : null, response.Usage);
    }

    /// <summary>The brief stage's question: does the brief cover the need as the user stated it. Advice for the user only.</summary>
    public async Task<ForgeUsageSnapshot> RateCoverageAsync(
        ForgeEventWriter events, ForgeBrief brief, IReadOnlyList<string> request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);
        if (!Enabled)
            return default;

        var question = new ScoreQuestion
        {
            Instructions = JsonValue.Create("Does the brief cover the need as the user stated it in the request?"),
            Criteria = [.. CoverageLevels.Select(static level => (JsonNode)JsonValue.Create(level)!)],
        };
        var response = await AskAsync(events, ForgeEventWriter.Spell(ForgeState.Brief), UseCoverage,
            StateOf(new { request, brief }), [KeyValuePair.Create<string, DecisionQuestion>(UseCoverage, question)], cancellationToken)
            .ConfigureAwait(false);
        return response.Usage;
    }

    /// <summary>The test stage's question, before the trial: will the crew complete it and meet the criteria. Advice for the user only.</summary>
    public async Task<ForgeUsageSnapshot> EstimateTrialAsync(
        ForgeEventWriter events, ForgeBrief? brief, ForgeBlueprint? blueprint, CancellationToken cancellationToken)
    {
        if (!Enabled)
            return default;

        var question = new NoulQuestion
        {
            Instructions = JsonValue.Create("The crew completes its trial and meets the acceptance criteria of the brief."),
        };
        var response = await AskAsync(events, ForgeEventWriter.Spell(ForgeState.Test), UseEstimate,
            StateOf(new { brief, blueprint }), [KeyValuePair.Create<string, DecisionQuestion>(UseEstimate, question)], cancellationToken)
            .ConfigureAwait(false);
        return response.Usage;
    }

    /// <summary>
    /// The diagnosis's questions, after the judge: one call per acceptance criterion, does the output meet it.
    /// Advice for the user only: never written into <c>verdict.json</c>.
    /// </summary>
    public async Task<ForgeUsageSnapshot> ReadCriteriaAsync(
        ForgeEventWriter events, ForgeBrief brief, string output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);
        if (!Enabled)
            return default;

        var usage = default(ForgeUsageSnapshot);
        var criteria = brief.Acceptance ?? [];
        for (var index = 0; index < criteria.Count; index++)
        {
            var criterion = criteria[index];
            var name = criterion.Id is { Length: > 0 } id ? id : $"A{index + 1}";
            var question = new NoulQuestion
            {
                Instructions = JsonValue.Create($"The output meets this acceptance criterion: {criterion.Statement}"),
            };
            var response = await AskAsync(events, ForgeEventWriter.Spell(ForgeState.Diagnose), UseCriteria,
                StateOf(new { criterion = new { id = name, statement = criterion.Statement }, output }),
                [KeyValuePair.Create<string, DecisionQuestion>(name, question)], cancellationToken)
                .ConfigureAwait(false);
            usage = usage.Plus(response.Usage);
        }

        return usage;
    }

    /// <summary>
    /// One call: the answer and its <c>systemone.answered</c> line, or, on a <see cref="DecisionModelException"/>,
    /// one <see cref="FailedWarning"/> and no answer. The tokens are the stage's to charge: input as prompt, output
    /// as completion, all of them System One's.
    /// </summary>
    private async Task<(DecisionResponse? Answer, ForgeUsageSnapshot Usage)> AskAsync(
        ForgeEventWriter events,
        string stage,
        string use,
        JsonNode state,
        IEnumerable<KeyValuePair<string, DecisionQuestion>> questions,
        CancellationToken cancellationToken)
    {
        var request = new DecisionRequest { State = state, Questions = questions.ToImmutableDictionary(StringComparer.Ordinal) };
        var started = Stopwatch.GetTimestamp();
        DecisionResponse response;
        try
        {
            response = await _model!.DecideAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (DecisionModelException ex)
        {
            events.Warning(FailedWarning, $"System One could not answer the {use} question of the {stage} stage ({ex.Message}): the stage carries on without its advice.");
            return (null, default);
        }

        var input = response.Usage.InputTokens ?? 0;
        var output = response.Usage.OutputTokens ?? 0;
        events.Emit(AnsweredKind, new
        {
            stage,
            use,
            questions = JsonSerializer.SerializeToNode(request.Questions),
            answers = CompactAnswers(response),
            model = response.Model,
            // A count the model did not report is written as zero, so the line always carries both.
            inputTokens = input,
            outputTokens = output,
            durationMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
        });
        return (response, new ForgeUsageSnapshot(input, output, 0) { SystemOneTokens = input + output });
    }

    /// <summary>The answers in the compact decision shape of a decision task's output (<see cref="DecisionOutcome.ToCompactJson"/>).</summary>
    internal static JsonNode? CompactAnswers(DecisionResponse response)
    {
        var values = new Dictionary<string, DecisionAnswerValue>(StringComparer.Ordinal);
        foreach (var (name, answer) in response.Answers)
        {
            values[name] = answer switch
            {
                ChoiceAnswer c => new DecisionAnswerValue { Chosen = c.Choice, Confidence = c.Confidence, Probabilities = c.Probabilities },
                ScoreAnswer s => new DecisionAnswerValue { Score = s.Score, Confidence = s.Confidence, Probabilities = s.Probabilities },
                NoulAnswer n => new DecisionAnswerValue
                {
                    Chosen = n.Noul >= 0.5 ? "yes" : "no",
                    Probability = n.Noul,
                    Confidence = Math.Max(n.Noul, 1 - n.Noul),
                },
                BoxAnswer b => new DecisionAnswerValue { Confidence = b.Confidence ?? 0 },
                _ => new DecisionAnswerValue(),
            };
        }

        return JsonNode.Parse(new DecisionOutcome { Answers = values }.ToCompactJson());
    }

    private static JsonNode StateOf(object state) => JsonSerializer.SerializeToNode(state, StateOptions)!;
}

/// <summary>System One's suggestion of a process for the blueprint (#435), as the assistant's inputs carry it.</summary>
/// <param name="Process">The suggested process, lowercase.</param>
/// <param name="P">The probability System One gave it.</param>
internal sealed record ForgeProcessHint(string Process, double P);
