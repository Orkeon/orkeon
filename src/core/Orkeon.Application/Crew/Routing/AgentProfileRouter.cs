using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Crew.Routing;

/// <summary>
/// The profile of a task of an agent on <c>llm.profile: auto</c> (#434): at the task's start, the decision model
/// picks one rung of the ladder (<see cref="AgentRouteOptions.Ladder"/>, cheapest first), and the task keeps it.
/// Under a probability of <see cref="MinProbability"/>, without an answer, with the decision circuit off (one
/// warning, no call) or without a ladder, the host's default profile stands. Nothing here fails a task. Unlike the
/// <c>route</c> stage of a decision profile, which routes each call on its last message, this picks once per task.
/// </summary>
internal sealed class AgentProfileRouter
{
    /// <summary>
    /// The probability of its pick under which the model is not followed, as in Orkeon Code
    /// (<c>ROUTE_MIN_CONFIDENCE</c>): between two rungs, a pick under 0.6 is barely ahead of the other, and the
    /// default profile is what the host chose.
    /// </summary>
    internal const double MinProbability = 0.6;

    /// <summary>The task, as the decision model reads it, in characters: cut in the middle, its start and end kept.</summary>
    internal const int RequestChars = 4_000;

    /// <summary>The name of the question.</summary>
    internal const string QuestionName = "profile";

    private readonly IServiceProvider _services;
    private readonly ImmutableList<string> _ladder;
    private readonly ILogger _logger;
    private int _offWarned;
    private int _noLadderWarned;

    /// <param name="services">The container whose decision circuit and model the router uses.</param>
    /// <param name="options">The ladder.</param>
    /// <param name="logger">Where the warnings go.</param>
    public AgentProfileRouter(IServiceProvider services, AgentRouteOptions options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _services = services;
        _ladder = [.. options.Ladder.Select(rung => rung.Trim())];
        _logger = logger;
    }

    /// <summary>The ladder, cheapest first.</summary>
    public ImmutableList<string> Ladder => _ladder;

    /// <summary>The profile <paramref name="agent"/> runs task <paramref name="taskId"/> on, and why.</summary>
    /// <param name="agent">The agent, on <c>llm.profile: auto</c>.</param>
    /// <param name="taskId">The task, for the notice.</param>
    /// <param name="request">The task as the decision model reads it (cut at <see cref="RequestChars"/>).</param>
    /// <param name="scopeCircuit">
    /// The circuit of the run's scope when it is not the container's (<see cref="DecisionCircuitOverride"/>, #431);
    /// null follows the container's.
    /// </param>
    /// <param name="cancellationToken">Cancels the decision call.</param>
    public async System.Threading.Tasks.Task<AgentRouteNotice> RouteAsync(
        DomainAgent agent, string taskId, string request, DecisionCircuit? scopeCircuit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var role = agent.Role.Value;
        var notice = await PickAsync(role, request, scopeCircuit, cancellationToken).ConfigureAwait(false) with
        {
            TaskId = taskId,
            AgentRole = role,
        };
        AgentRouteLog.Routed(_logger, role, taskId, notice.Profile, notice.Reason);
        return notice;
    }

    private async System.Threading.Tasks.Task<AgentRouteNotice> PickAsync(
        string role, string request, DecisionCircuit? scopeCircuit, CancellationToken cancellationToken)
    {
        if (_ladder.Count == 0)
        {
            if (Interlocked.Exchange(ref _noLadderWarned, 1) == 0)
                AgentRouteLog.NoLadder(_logger, role, LlmProfiles.RouteLadderKey);
            return Standing(AgentRouteReasons.NoLadder);
        }

        var circuit = DecisionCircuit.Of(_services, scopeCircuit);
        var model = circuit.Enabled ? _services.GetService<IDecisionModel>() : null;
        if (model is null)
        {
            if (Interlocked.Exchange(ref _offWarned, 1) == 0)
                AgentRouteLog.CircuitOff(_logger, circuit.OffReason ?? DecisionCircuit.OffBy(DecisionCircuitCause.NoProvider).OffReason!);
            return Standing(AgentRouteReasons.CircuitOff);
        }

        DecisionResponse response;
        try
        {
            response = await model.DecideAsync(Question(_ladder, role, request), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            AgentRouteLog.PickFailed(_logger, role, ex.Message);
            return Standing(AgentRouteReasons.NoAnswer);
        }

        return Decide(response, _ladder);
    }

    private AgentRouteNotice Standing(string reason) => Notice(LlmProfiles.Default, reason, null, null, _ladder);

    private static AgentRouteNotice Notice(string profile, string reason, string? picked, double? probability, ImmutableList<string> ladder) =>
        new()
        {
            TaskId = string.Empty,
            AgentRole = string.Empty,
            Profile = profile,
            Reason = reason,
            Picked = picked,
            Probability = probability,
            Ladder = ladder,
        };

    /// <summary>
    /// The profile an answer gives: its rung when the probability the answer gives it (else the answer's own
    /// confidence) reaches <see cref="MinProbability"/>, the default profile otherwise.
    /// </summary>
    internal static AgentRouteNotice Decide(DecisionResponse response, ImmutableList<string> ladder)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!response.Answers.TryGetValue(QuestionName, out var answer) || answer is not ChoiceAnswer choice
            || !ladder.Contains(choice.Choice, StringComparer.OrdinalIgnoreCase))
            return Notice(LlmProfiles.Default, AgentRouteReasons.NoAnswer, null, null, ladder);

        var rung = ladder.First(r => string.Equals(r, choice.Choice, StringComparison.OrdinalIgnoreCase));
        var probability = choice.Probabilities.TryGetValue(choice.Choice, out var given) ? given : choice.Confidence;
        return probability >= MinProbability
            ? Notice(rung, AgentRouteReasons.Model, rung, probability, ladder)
            : Notice(LlmProfiles.Default, AgentRouteReasons.LowConfidence, rung, probability, ladder);
    }

    /// <summary>One choice among the rungs, each described by its place on the ladder.</summary>
    internal static DecisionRequest Question(IReadOnlyList<string> ladder, string role, string request)
    {
        var criteria = ImmutableDictionary.CreateBuilder<string, JsonNode?>(StringComparer.Ordinal);
        for (var i = 0; i < ladder.Count; i++)
            criteria[ladder[i]] = JsonValue.Create(i == 0 ? RoutePrompts.Lowest : i == ladder.Count - 1 ? RoutePrompts.Highest : RoutePrompts.Middle);
        return new DecisionRequest
        {
            State = JsonValue.Create(RoutePrompts.State(role, MiddleCut(request.Trim(), RequestChars)))!,
            Questions = ImmutableDictionary<string, DecisionQuestion>.Empty.Add(QuestionName, new ChoiceQuestion
            {
                Instructions = JsonValue.Create(RoutePrompts.QuestionInstructions),
                Criteria = criteria.ToImmutable(),
            }),
        };
    }

    /// <summary><paramref name="text"/> within <paramref name="max"/> characters, its start and its end kept.</summary>
    internal static string MiddleCut(string text, int max)
    {
        if (text.Length <= max)
            return text;
        var half = (max - RoutePrompts.Elision.Length) / 2;
        return string.Concat(text.AsSpan(0, half), RoutePrompts.Elision, text.AsSpan(text.Length - half));
    }
}
