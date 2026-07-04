using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Dlp;

/// <summary>
/// Configuration options for the DLP system.
/// </summary>
public record DlpOptions
{
    /// <summary>Gets whether DLP is globally enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Gets the default action for all channels.</summary>
    public DlpAction DefaultAction { get; init; } = DlpAction.Audit;

    /// <summary>Gets per-channel policy overrides.</summary>
    public Dictionary<DlpChannel, DlpPolicy> ChannelPolicies { get; init; } = [];
}
