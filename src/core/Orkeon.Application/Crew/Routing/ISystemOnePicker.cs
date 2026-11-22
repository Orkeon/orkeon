using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Application.Crew.Routing;

/// <summary>
/// System One's profile and thinking-level pick for a caller that runs no task (#460): what
/// <c>ctx.llm.pick(request, { profile, effort })</c> asks. The pick of a task on <c>llm.profile: auto</c> and
/// <c>effort: auto</c>, per call: the ladder, its floor and ceiling, the levels and the fallbacks are the caller's,
/// one decision call answers both, and the host keeps nothing - the answer is a value the caller holds as long as it
/// chooses. With the decision circuit off, or in a crew on <c>decision.mode: none</c>, no call is made and the
/// fallbacks stand.
/// </summary>
[Experimental("ORKEXP008", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public interface ISystemOnePicker
{
    /// <summary>What System One picks for <paramref name="request"/>.</summary>
    /// <param name="request">What to pick, and among what.</param>
    /// <param name="cancellationToken">Cancels the decision call.</param>
    /// <returns>An answer for each part asked, the other null.</returns>
    /// <exception cref="ArgumentException">
    /// The request is refused before any decision call; <see cref="ArgumentException.ParamName"/> names the option
    /// (<c>profile.ladder</c>, <c>profile.floor</c>, <c>effort.min</c>, ...).
    /// </exception>
    System.Threading.Tasks.Task<SystemOnePickResponse> PickAsync(SystemOnePickRequest request, CancellationToken cancellationToken);
}

/// <summary>The input of <see cref="ISystemOnePicker.PickAsync"/>: the text the decision model reads, and what to pick.</summary>
[Experimental("ORKEXP008", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record SystemOnePickRequest
{
    /// <summary>What the decision model reads, cut in the middle at 4 000 characters.</summary>
    public required string Request { get; init; }

    /// <summary>The role of the agent the pick is for, as the decision model reads it and the notices carry it.</summary>
    public required string AgentRole { get; init; }

    /// <summary>The profile pick; null asks none.</summary>
    public ProfilePickRequest? Profile { get; init; }

    /// <summary>The thinking-level pick; null asks none.</summary>
    public EffortPickRequest? Effort { get; init; }
}

/// <summary>The profile part of a <see cref="SystemOnePickRequest"/>.</summary>
[Experimental("ORKEXP008", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record ProfilePickRequest
{
    /// <summary>
    /// The rungs, cheapest first: two or more profiles the host declares, each once, none blank nor <c>auto</c>.
    /// Null takes <c>Orkeon:Decision:Route:Ladder</c>.
    /// </summary>
    public ImmutableList<string>? Ladder { get; init; }

    /// <summary>The lowest rung the answer may be: a rung of the ladder; null sets no floor.</summary>
    public string? Floor { get; init; }

    /// <summary>The highest rung the answer may be: a rung of the ladder; null sets no ceiling.</summary>
    public string? Ceiling { get; init; }

    /// <summary>
    /// What stands without a pick (under 0.6, no answer, a failed call, the circuit off): a profile the host
    /// declares, on the ladder or not. Null takes the host's <c>default</c> profile.
    /// </summary>
    public string? Fallback { get; init; }
}

/// <summary>The thinking-level part of a <see cref="SystemOnePickRequest"/>.</summary>
[Experimental("ORKEXP008", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record EffortPickRequest
{
    /// <summary>The lowest level offered among <c>Orkeon:Decision:Effort:Levels</c>; null sets no bound.</summary>
    public string? Min { get; init; }

    /// <summary>The highest level offered among <c>Orkeon:Decision:Effort:Levels</c>; null sets no bound.</summary>
    public string? Max { get; init; }

    /// <summary>The level that stands without a pick; null answers no level, and the profile's own thinking stands.</summary>
    public string? Fallback { get; init; }
}

/// <summary>The answer of <see cref="ISystemOnePicker.PickAsync"/>.</summary>
[Experimental("ORKEXP008", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record SystemOnePickResponse
{
    /// <summary>The profile answer, when the profile was asked; null otherwise.</summary>
    public SystemOnePickAnswer? Profile { get; init; }

    /// <summary>The thinking-level answer, when the effort was asked; null otherwise.</summary>
    public SystemOnePickAnswer? Effort { get; init; }
}

/// <summary>One answer of a pick.</summary>
[Experimental("ORKEXP008", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed record SystemOnePickAnswer
{
    /// <summary>
    /// The profile or the level to use: the model's pick (clamped by the floor and the ceiling), else the fallback.
    /// Null for an effort without a fallback.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>One of <see cref="SystemOnePickReasons"/>.</summary>
    public required string Reason { get; init; }

    /// <summary>What the decision model answered, when it answered one of the options; null otherwise.</summary>
    public string? Picked { get; init; }

    /// <summary>The probability the answer gave <see cref="Picked"/>; null when it picked nothing.</summary>
    public double? Probability { get; init; }
}

/// <summary>
/// The <c>reason</c> of a <see cref="SystemOnePickAnswer"/>: those of <c>agent.route</c> and <c>agent.effort</c> that
/// need no task nor crew mode, and the two clamps.
/// </summary>
[Experimental("ORKEXP008", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public static class SystemOnePickReasons
{
    /// <summary>The decision model's pick, at a probability of 0.6 or more.</summary>
    public const string Model = Orkeon.Application.Interfaces.Ports.AgentRouteReasons.Model;

    /// <summary>The pick's probability is under 0.6: the fallback stands.</summary>
    public const string LowConfidence = Orkeon.Application.Interfaces.Ports.AgentRouteReasons.LowConfidence;

    /// <summary>The decision model gave none of the options, or its call failed: the fallback stands.</summary>
    public const string NoAnswer = Orkeon.Application.Interfaces.Ports.AgentRouteReasons.NoAnswer;

    /// <summary>The decision circuit is off, or the crew runs on <c>decision.mode: none</c>: no call, the fallback stands.</summary>
    public const string CircuitOff = Orkeon.Application.Interfaces.Ports.AgentRouteReasons.CircuitOff;

    /// <summary>No ladder is given and the host declares none: the profile fallback stands.</summary>
    public const string NoLadder = Orkeon.Application.Interfaces.Ports.AgentRouteReasons.NoLadder;

    /// <summary>The pick was under the floor, or the floor is the one rung left: the floor stands.</summary>
    public const string Floor = "floor";

    /// <summary>The pick was above the ceiling, or the ceiling is the one rung left: the ceiling stands.</summary>
    public const string Ceiling = "ceiling";

    /// <summary>One level is left between the bounds: it stands, with no call.</summary>
    public const string SingleLevel = Orkeon.Application.Interfaces.Ports.AgentEffortReasons.SingleLevel;
}
