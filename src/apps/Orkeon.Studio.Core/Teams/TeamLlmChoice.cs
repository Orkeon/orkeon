using System.Text.Json.Serialization;

namespace Orkeon.Studio.Core.Teams;

/// <summary>
/// The model a team runs on (STUDIO-73): the machine's default, whatever it is when the run starts,
/// or one setting pinned by its name. What a team's companion file says (<see cref="TeamCatalog.LlmChoiceFor"/>),
/// what the Run screen chooses for one run, and what the launch history keeps for « Relancer ».
/// </summary>
/// <param name="UseDefault">Whether the run takes the default elected when it starts.</param>
/// <param name="Profile">The setting pinned, by its name; null when <paramref name="UseDefault"/>.</param>
public sealed record TeamLlmChoice(
    [property: JsonPropertyName("use_default")] bool UseDefault,
    [property: JsonPropertyName("profile")] string? Profile)
{
    /// <summary>The default, followed.</summary>
    public static TeamLlmChoice Default { get; } = new(true, null);

    /// <summary>The setting <paramref name="profile"/>, pinned; the default when it is blank.</summary>
    public static TeamLlmChoice Pinned(string? profile) =>
        profile is { Length: > 0 } ? new TeamLlmChoice(false, profile) : Default;

    /// <summary>
    /// The choice a companion file spells: the flag <c>useDefaultLlm</c> wins over a name left beside
    /// it; without the flag, a name pins the team (an adoption before STUDIO-73, D3) and no name reads
    /// as the default, which is what the run takes.
    /// </summary>
    public static TeamLlmChoice Of(StudioTeamMetadata? metadata) =>
        metadata is { UseDefaultLlm: true } ? Default : Pinned(metadata?.Profile);
}
