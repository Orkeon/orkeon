using System.Collections.Immutable;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Application.Crew.Routing;

/// <summary>
/// <see cref="ISystemOnePicker"/> over the router of the tasks (<see cref="AgentProfileRouter"/>), per call (#460): the
/// request's ladder, floor, ceiling and fallbacks, the host's ladder and levels when it gives none. Every refusal is
/// thrown before any decision call. The answers are told to the run's observer (<c>agent.route</c>,
/// <c>agent.effort</c>) with no task id: a pick belongs to no task.
/// </summary>
internal sealed class SystemOnePicker : ISystemOnePicker
{
    private readonly AgentProfileRouter _router;
    private readonly ILlmProfileRegistry? _profiles;
    private readonly IAgentRouteObserver? _observer;
    private readonly DecisionCircuit? _scopeCircuit;

    /// <summary>Creates the pick over a router's ladder, levels, circuit and decision model.</summary>
    /// <param name="router">The router whose ladder, levels, circuit and decision model the pick uses.</param>
    /// <param name="profiles">The host's profiles; null offers the default alone.</param>
    /// <param name="observer">Told each answer; null logs nothing more.</param>
    /// <param name="scopeCircuit">The circuit of the run's scope when it is not the container's; null follows the container's.</param>
    public SystemOnePicker(AgentProfileRouter router, ILlmProfileRegistry? profiles, IAgentRouteObserver? observer, DecisionCircuit? scopeCircuit)
    {
        ArgumentNullException.ThrowIfNull(router);
        _router = router;
        _profiles = profiles;
        _observer = observer;
        _scopeCircuit = scopeCircuit;
    }

    /// <inheritdoc />
    public async System.Threading.Tasks.Task<SystemOnePickResponse> PickAsync(SystemOnePickRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Profile is null && request.Effort is null)
            throw Refused("options", "asks neither a profile nor an effort: give { profile } or { effort }, or both.");

        var profile = request.Profile is { } p ? ProfilePlan(p) : null;
        var effort = request.Effort is { } e ? EffortPlan(e) : null;
        cancellationToken.ThrowIfCancellationRequested();

        // What needs no call: one rung or one level left, or no ladder at all.
        var profileAnswer = profile?.Fixed;
        var effortAnswer = effort?.Fixed;
        var askProfile = profile is not null && profileAnswer is null;
        var askEffort = effort is not null && effortAnswer is null;

        // A crew on decision.mode: none asks System One nothing: for a script, the circuit is off (#460).
        if ((askProfile || askEffort) && SystemOneRunScope.Current?.Mode == CrewDecisionMode.None)
        {
            if (askProfile)
                profileAnswer = new SystemOnePickAnswer { Value = profile!.Fallback, Reason = SystemOnePickReasons.CircuitOff };
            if (askEffort)
                effortAnswer = new SystemOnePickAnswer { Value = effort!.Fallback, Reason = SystemOnePickReasons.CircuitOff };
            askProfile = askEffort = false;
        }

        if (askProfile || askEffort)
        {
            var route = await _router.PickAsync(
                request.AgentRole,
                request.Request ?? string.Empty,
                askProfile ? profile!.Ladder : null,
                askProfile ? profile!.Fallback : LlmProfiles.Default,
                askEffort ? effort!.Levels : null,
                _scopeCircuit,
                cancellationToken).ConfigureAwait(false);
            if (askProfile && route.Profile is { } routed)
                profileAnswer = profile!.Clamp(routed);
            if (askEffort && route.Effort is { } picked)
                effortAnswer = new SystemOnePickAnswer
                {
                    Value = picked.Level ?? effort!.Fallback,
                    Reason = picked.Reason,
                    Picked = picked.Picked,
                    Probability = picked.Probability,
                };
        }

        if (profileAnswer is not null)
            _observer?.ProfileRouted(new AgentRouteNotice
            {
                TaskId = string.Empty,
                AgentRole = request.AgentRole,
                Profile = profileAnswer.Value ?? LlmProfiles.Default,
                Reason = profileAnswer.Reason,
                Picked = profileAnswer.Picked,
                Probability = profileAnswer.Probability,
                Ladder = profile!.Ladder,
            });
        if (effortAnswer is not null)
            _observer?.EffortPicked(new AgentEffortNotice
            {
                TaskId = string.Empty,
                AgentRole = request.AgentRole,
                Level = effortAnswer.Value,
                Reason = effortAnswer.Reason,
                Picked = effortAnswer.Picked,
                Probability = effortAnswer.Probability,
                Levels = effort!.Levels,
            });

        return new SystemOnePickResponse { Profile = profileAnswer, Effort = effortAnswer };
    }

    private ProfilePlanned ProfilePlan(ProfilePickRequest options)
    {
        ImmutableList<string> ladder;
        if (options.Ladder is null)
        {
            ladder = _router.Ladder;
        }
        else
        {
            if (options.Ladder.Count < 2)
                throw Refused("profile.ladder", $"names {options.Ladder.Count} rung(s), and a ladder needs two or more, cheapest first.");
            var rungs = ImmutableList.CreateBuilder<string>();
            foreach (var rung in options.Ladder)
            {
                var name = Declared(rung, "profile.ladder");
                if (rungs.Any(r => string.Equals(r, name, StringComparison.OrdinalIgnoreCase)))
                    throw Refused("profile.ladder", $"names the profile '{name}' more than once: each rung is a different profile.");
                rungs.Add(name);
            }

            ladder = rungs.ToImmutable();
        }

        var fallback = options.Fallback is null ? LlmProfiles.Default : Declared(options.Fallback, "profile.fallback");
        var floor = options.Floor is null ? -1 : RungOf(ladder, options.Floor, "profile.floor");
        var ceiling = options.Ceiling is null ? -1 : RungOf(ladder, options.Ceiling, "profile.ceiling");
        if (floor >= 0 && ceiling >= 0 && floor > ceiling)
            throw Refused("profile.floor", $"'{ladder[floor]}' is above the ceiling '{ladder[ceiling]}' on the ladder ({string.Join(", ", ladder)}).");

        SystemOnePickAnswer? fixedAnswer = null;
        if (ladder.Count == 0)
        {
            fixedAnswer = new SystemOnePickAnswer { Value = fallback, Reason = SystemOnePickReasons.NoLadder };
        }
        else
        {
            var low = floor >= 0 ? floor : 0;
            var high = ceiling >= 0 ? ceiling : ladder.Count - 1;
            if (low == high)
                fixedAnswer = new SystemOnePickAnswer
                {
                    Value = ladder[low],
                    Reason = floor >= 0 ? SystemOnePickReasons.Floor : SystemOnePickReasons.Ceiling,
                };
        }

        return new ProfilePlanned(ladder, fallback, floor, ceiling, fixedAnswer);
    }

    private EffortPlanned EffortPlan(EffortPickRequest options)
    {
        var min = options.Min is null ? null : Level(options.Min, "effort.min");
        var max = options.Max is null ? null : Level(options.Max, "effort.max");
        var fallback = options.Fallback is null ? null : Level(options.Fallback, "effort.fallback");
        var low = min is null ? int.MinValue : LlmThinkingConfig.RankOf(min);
        var high = max is null ? int.MaxValue : LlmThinkingConfig.RankOf(max);
        if (low > high)
            throw Refused("effort.min", $"'{min}' is above max '{max}'.");

        var levels = ImmutableList.CreateRange(_router.EffortLevels.Where(level =>
            LlmThinkingConfig.RankOf(level) >= low && LlmThinkingConfig.RankOf(level) <= high));
        if (levels.Count == 0)
            throw Refused("effort", $"leaves no level of {AgentEffortOptions.LevelsKey} ({string.Join(", ", _router.EffortLevels)}) between " +
                $"min '{min ?? "(none)"}' and max '{max ?? "(none)"}'.");

        var fixedAnswer = levels.Count == 1
            ? new SystemOnePickAnswer { Value = levels[0], Reason = SystemOnePickReasons.SingleLevel }
            : null;
        return new EffortPlanned(levels, fallback, fixedAnswer);
    }

    /// <summary>The refusal of <paramref name="option"/>: <see cref="ArgumentException.ParamName"/> names it.</summary>
    private static ArgumentException Refused(string option, string message) => new(message, option);

    /// <summary>A profile the host declares, as it spells it.</summary>
    private string Declared(string? name, string option)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw Refused(option, "is blank: write a profile name.");
        if (LlmProfiles.IsAuto(trimmed))
            throw Refused(option, $"is '{LlmProfiles.Auto}', the name of the pick itself: write a profile name.");
        if (LlmProfiles.IsDefault(trimmed))
            return LlmProfiles.Default;
        if (_profiles?.Names.FirstOrDefault(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase)) is { } known)
            return known;
        if (_profiles?.IsKnown(trimmed) == true)
            return trimmed;
        throw Refused(option, LlmProfiles.UnknownMessage(trimmed, option, _profiles?.Names ?? []));
    }

    private static int RungOf(ImmutableList<string> ladder, string name, string option)
    {
        var trimmed = name.Trim();
        var index = ladder.FindIndex(r => string.Equals(r, trimmed, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw Refused(option, $"'{trimmed}' is not a rung of the ladder ({(ladder.Count == 0 ? "none: the host declares no ladder" : string.Join(", ", ladder))}).");
        return index;
    }

    private static string Level(string name, string option)
    {
        var trimmed = name.Trim();
        return LlmThinkingConfig.Levels.FirstOrDefault(l => string.Equals(l, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? throw Refused(option, $"'{trimmed}' is not a thinking level: write one of {string.Join(", ", LlmThinkingConfig.Levels)}.");
    }

    private sealed record ProfilePlanned(ImmutableList<string> Ladder, string Fallback, int Floor, int Ceiling, SystemOnePickAnswer? Fixed)
    {
        /// <summary>The router's answer, held between the floor and the ceiling; the fallback is never clamped.</summary>
        public SystemOnePickAnswer Clamp(AgentRouteNotice routed)
        {
            var answer = new SystemOnePickAnswer
            {
                Value = routed.Profile,
                Reason = routed.Reason,
                Picked = routed.Picked,
                Probability = routed.Probability,
            };
            if (routed.Reason != AgentRouteReasons.Model)
                return answer;
            var index = Ladder.FindIndex(r => string.Equals(r, routed.Profile, StringComparison.OrdinalIgnoreCase));
            if (Floor >= 0 && index < Floor)
                return answer with { Value = Ladder[Floor], Reason = SystemOnePickReasons.Floor };
            if (Ceiling >= 0 && index > Ceiling)
                return answer with { Value = Ladder[Ceiling], Reason = SystemOnePickReasons.Ceiling };
            return answer;
        }
    }

    private sealed record EffortPlanned(ImmutableList<string> Levels, string? Fallback, SystemOnePickAnswer? Fixed);
}
