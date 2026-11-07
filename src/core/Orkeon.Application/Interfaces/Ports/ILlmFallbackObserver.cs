namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Told when a profile's fallback chain moves a call from one profile to the next (FWK-05). The
/// run protocol writes it as <c>llm.fallback</c>; a host that registers none loses only that line,
/// the switch is logged either way.
/// </summary>
public interface ILlmFallbackObserver
{
    /// <summary>A call left <paramref name="fromProfile"/> for <paramref name="toProfile"/>.</summary>
    /// <param name="profile">The profile whose chain switched: the one a crew named.</param>
    /// <param name="fromProfile">The profile that failed.</param>
    /// <param name="toProfile">The profile the call goes to next.</param>
    /// <param name="reason">The failure class (<c>server_error</c>, <c>rate_limited</c>, <c>timeout</c>, <c>transport</c>, <c>exhausted</c>).</param>
    void FallbackTaken(string profile, string fromProfile, string toProfile, string reason);
}
