using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Crew.Routing;

/// <summary>
/// The profile of a task of an agent on <c>llm.profile: auto</c> (#434): at the task's start, the decision model
/// picks one rung of the ladder (<see cref="AgentRouteOptions.Ladder"/>, cheapest first), and the task keeps it.
/// Under a probability of <see cref="MinProbability"/>, without an answer, with the decision circuit off (one
/// warning per run, no call), in a crew on <c>decision.mode: none</c> (no call, no warning) or without a ladder, the
/// host's <c>default</c> profile stands, never the agent's own. Nothing here fails a task. Unlike the
/// <c>route</c> stage of a decision profile, which routes each call on its last message, this picks once per task.
/// It also picks the thinking level of a task on <c>thinking: { effort: auto }</c> (#439) among the host's levels
/// (<see cref="AgentEffortOptions"/>), in the same call when the profile is picked too: each answer has its own floor
/// and its own fallback, and its own warnings.
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

    /// <summary>The name of the thinking-level question (#439).</summary>
    internal const string EffortQuestionName = "effort";

    private readonly IServiceProvider _services;
    private readonly ImmutableList<string> _ladder;
    private readonly ImmutableList<string> _effortLevels;
    private readonly ILogger _logger;
    private readonly RunWarnings _warnings = new();

    /// <param name="services">The container whose decision circuit and model the router uses.</param>
    /// <param name="options">The ladder.</param>
    /// <param name="logger">Where the warnings go.</param>
    public AgentProfileRouter(IServiceProvider services, AgentRouteOptions options, ILogger logger)
        : this(services, options, new AgentEffortOptions(), logger)
    {
    }

    /// <param name="services">The container whose decision circuit and model the router uses.</param>
    /// <param name="options">The ladder.</param>
    /// <param name="effort">The thinking levels the host allows (#439).</param>
    /// <param name="logger">Where the warnings go.</param>
    public AgentProfileRouter(IServiceProvider services, AgentRouteOptions options, AgentEffortOptions effort, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(effort);
        ArgumentNullException.ThrowIfNull(logger);
        _services = services;
        _ladder = [.. options.Ladder.Select(rung => rung.Trim())];
        _effortLevels = [.. AgentEffortOptions.AllowedLevels(effort)];
        _logger = logger;
    }

    /// <summary>The ladder, cheapest first.</summary>
    public ImmutableList<string> Ladder => _ladder;

    /// <summary>The thinking levels the host allows, cheapest first (#439).</summary>
    public ImmutableList<string> EffortLevels => _effortLevels;

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
        DomainAgent agent, string taskId, string request, DecisionCircuit? scopeCircuit, CancellationToken cancellationToken) =>
        (await RouteAsync(agent, taskId, request, routeProfile: true, effort: null, scopeCircuit, cancellationToken).ConfigureAwait(false)).Profile!;

    /// <summary>
    /// What System One picks for task <paramref name="taskId"/> of <paramref name="agent"/>: its profile when
    /// <paramref name="routeProfile"/>, its thinking level when <paramref name="effort"/> is given (#439), both in one
    /// decision call when both are asked.
    /// </summary>
    /// <param name="agent">The agent.</param>
    /// <param name="taskId">The task, for the notices.</param>
    /// <param name="request">The task as the decision model reads it (cut at <see cref="RequestChars"/>).</param>
    /// <param name="routeProfile">Whether the agent is on <c>llm.profile: auto</c>.</param>
    /// <param name="effort">The effort left to System One, null when it is not.</param>
    /// <param name="scopeCircuit">The circuit of the run's scope when it is not the container's; null follows the container's.</param>
    /// <param name="cancellationToken">Cancels the decision call.</param>
    /// <returns>The profile notice when the profile was asked, the effort pick when the effort was.</returns>
    public async System.Threading.Tasks.Task<TaskRoute> RouteAsync(
        DomainAgent agent, string taskId, string request, bool routeProfile, LlmAutoEffort? effort,
        DecisionCircuit? scopeCircuit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        var role = agent.Role.Value;
        var levels = effort is null ? null : ImmutableList.CreateRange(effort.Allowed(_effortLevels));
        var route = await PickAsync(role, request, routeProfile, levels, scopeCircuit, cancellationToken).ConfigureAwait(false);
        var profile = route.Profile is { } picked ? picked with { TaskId = taskId, AgentRole = role } : null;
        if (profile is not null)
            AgentRouteLog.Routed(_logger, role, taskId, profile.Profile, profile.Reason);
        if (route.Effort is { } level)
            AgentRouteLog.EffortPicked(_logger, role, taskId, level.Level ?? "(the one without auto)", level.Reason);
        return new TaskRoute(profile, route.Effort);
    }

    private async System.Threading.Tasks.Task<TaskRoute> PickAsync(
        string role, string request, bool routeProfile, ImmutableList<string>? levels, DecisionCircuit? scopeCircuit, CancellationToken cancellationToken)
    {
        // One level allowed is no choice (#439): that level, with no call; none left, the effort without auto.
        var askEffort = levels is { Count: > 1 };
        EffortPick? fixedEffort = null;
        if (levels is not null && !askEffort)
            fixedEffort = levels.Count == 1
                ? new EffortPick(levels[0], AgentEffortReasons.SingleLevel, null, null, levels)
                : new EffortPick(null, AgentEffortReasons.NoAnswer, null, null, levels);
        if (!routeProfile && !askEffort)
            return new TaskRoute(null, fixedEffort);

        // A crew on decision.mode: none asks System One nothing (#429), as its pruning does: no call, no warning.
        if (SystemOneRunScope.Current?.Mode == CrewDecisionMode.None)
            return new TaskRoute(
                routeProfile ? Standing(AgentRouteReasons.DecisionNone) : null,
                askEffort ? EffortStanding(AgentEffortReasons.DecisionNone, levels!) : fixedEffort);

        AgentRouteNotice? unrouted = null;
        if (routeProfile && _ladder.Count == 0)
        {
            if (RunWarnings.Of(_warnings).First("route.no-ladder"))
                AgentRouteLog.NoLadder(_logger, role, LlmProfiles.RouteLadderKey);
            unrouted = Standing(AgentRouteReasons.NoLadder);
            routeProfile = false;
        }

        if (!routeProfile && !askEffort)
            return new TaskRoute(unrouted, fixedEffort);

        var circuit = DecisionCircuit.Of(_services, scopeCircuit);
        var model = circuit.Enabled ? _services.GetService<IDecisionModel>() : null;
        if (model is null)
        {
            var reason = circuit.OffReason ?? DecisionCircuit.OffBy(DecisionCircuitCause.NoProvider).OffReason!;
            if (routeProfile && RunWarnings.Of(_warnings).First("route.circuit-off"))
                AgentRouteLog.CircuitOff(_logger, reason);
            if (askEffort && RunWarnings.Of(_warnings).First("effort.circuit-off"))
                AgentRouteLog.EffortCircuitOff(_logger, reason);
            return new TaskRoute(
                routeProfile ? Standing(AgentRouteReasons.CircuitOff) : unrouted,
                askEffort ? EffortStanding(AgentEffortReasons.CircuitOff, levels!) : fixedEffort);
        }

        DecisionResponse response;
        try
        {
            response = await model.DecideAsync(
                Question(routeProfile ? _ladder : null, askEffort ? levels : null, role, request), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (routeProfile)
                AgentRouteLog.PickFailed(_logger, role, ex.Message);
            if (askEffort)
                AgentRouteLog.EffortPickFailed(_logger, role, ex.Message);
            return new TaskRoute(
                routeProfile ? Standing(AgentRouteReasons.NoAnswer) : unrouted,
                askEffort ? EffortStanding(AgentEffortReasons.NoAnswer, levels!) : fixedEffort);
        }

        return new TaskRoute(
            routeProfile ? Decide(response, _ladder) : unrouted,
            askEffort ? DecideEffort(response, levels!) : fixedEffort);
    }

    private AgentRouteNotice Standing(string reason) => Notice(LlmProfiles.Default, reason, null, null, _ladder);

    private static EffortPick EffortStanding(string reason, ImmutableList<string> levels) => new(null, reason, null, null, levels);

    /// <summary>
    /// The level an answer gives (#439): its level when the probability the answer gives it (else the answer's own
    /// confidence) reaches <see cref="MinProbability"/>; no level otherwise, and the effort without <c>auto</c> stands.
    /// </summary>
    internal static EffortPick DecideEffort(DecisionResponse response, ImmutableList<string> levels)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!response.Answers.TryGetValue(EffortQuestionName, out var answer) || answer is not ChoiceAnswer choice
            || !levels.Contains(choice.Choice, StringComparer.OrdinalIgnoreCase))
            return EffortStanding(AgentEffortReasons.NoAnswer, levels);

        var level = levels.First(l => string.Equals(l, choice.Choice, StringComparison.OrdinalIgnoreCase));
        var probability = choice.Probabilities.TryGetValue(choice.Choice, out var given) ? given : choice.Confidence;
        return probability >= MinProbability
            ? new EffortPick(level, AgentEffortReasons.Model, level, probability, levels)
            : new EffortPick(null, AgentEffortReasons.LowConfidence, level, probability, levels);
    }

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
    internal static DecisionRequest Question(IReadOnlyList<string> ladder, string role, string request) =>
        Question(ladder, null, role, request);

    /// <summary>
    /// One request of up to two choices (#439): among the rungs of <paramref name="ladder"/> when given, among the
    /// thinking levels of <paramref name="levels"/> when given, each option described by its place, cheapest first.
    /// </summary>
    internal static DecisionRequest Question(IReadOnlyList<string>? ladder, IReadOnlyList<string>? levels, string role, string request)
    {
        var questions = ImmutableDictionary.CreateBuilder<string, DecisionQuestion>(StringComparer.Ordinal);
        if (ladder is not null)
            questions[QuestionName] = Choice(RoutePrompts.QuestionInstructions, ladder, RoutePrompts.Lowest, RoutePrompts.Middle, RoutePrompts.Highest);
        if (levels is not null)
            questions[EffortQuestionName] = Choice(EffortPrompts.QuestionInstructions, levels, EffortPrompts.Lowest, EffortPrompts.Middle, EffortPrompts.Highest);
        return new DecisionRequest
        {
            State = JsonValue.Create(RoutePrompts.State(role, MiddleCut(request.Trim(), RequestChars)))!,
            Questions = questions.ToImmutable(),
        };
    }

    private static ChoiceQuestion Choice(string instructions, IReadOnlyList<string> options, string lowest, string middle, string highest)
    {
        var criteria = ImmutableDictionary.CreateBuilder<string, JsonNode?>(StringComparer.Ordinal);
        for (var i = 0; i < options.Count; i++)
            criteria[options[i]] = JsonValue.Create(i == 0 ? lowest : i == options.Count - 1 ? highest : middle);
        return new ChoiceQuestion
        {
            Instructions = JsonValue.Create(instructions),
            Criteria = criteria.ToImmutable(),
        };
    }

    /// <summary><paramref name="text"/> within <paramref name="max"/> characters, its start and its end kept.</summary>
    internal static string MiddleCut(string text, int max) => TextCut.Middle(text, max, RoutePrompts.Elision);
}

/// <summary>What System One picked at a task's start for an agent (#434, #439): its profile, its thinking level.</summary>
/// <param name="Profile">The profile notice, when the profile was asked; null otherwise.</param>
/// <param name="Effort">The thinking-level pick, when the effort was asked; null otherwise.</param>
internal sealed record TaskRoute(AgentRouteNotice? Profile, EffortPick? Effort);

/// <summary>The thinking level picked for a task (#439).</summary>
/// <param name="Level">The level the task runs with; null when none was picked and the effort without <c>auto</c> stands.</param>
/// <param name="Reason">One of <see cref="AgentEffortReasons"/>.</param>
/// <param name="Picked">The level the decision model answered, when it answered one of the levels offered.</param>
/// <param name="Probability">The probability the answer gave <paramref name="Picked"/>.</param>
/// <param name="Levels">The levels offered, cheapest first.</param>
internal sealed record EffortPick(string? Level, string Reason, string? Picked, double? Probability, ImmutableList<string> Levels);
