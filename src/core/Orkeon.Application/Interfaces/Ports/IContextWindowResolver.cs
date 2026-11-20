using Orkeon.Application.Crew.Compaction;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Resolves the context window a loop compacts against (#432): the profile's own
/// <c>ContextWindowTokens</c>, else the documented window of the model, else
/// <c>Orkeon:Cli:Session:ContextWindowTokens</c> with one warning per profile naming the figure assumed.
/// </summary>
public interface IContextWindowResolver
{
    /// <summary>The window of <paramref name="model"/> on <paramref name="profile"/>.</summary>
    /// <param name="profile">The profile the loop runs on (<c>default</c> for the host's own).</param>
    /// <param name="model">The model the call names, or null when it leaves it to the profile.</param>
    /// <param name="profileWindowTokens">The profile's or the agent's <c>ContextWindowTokens</c>, or null.</param>
    ContextWindow Resolve(string profile, string? model, int? profileWindowTokens);
}
