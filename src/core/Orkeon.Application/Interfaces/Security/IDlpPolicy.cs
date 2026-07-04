namespace Orkeon.Application.Interfaces.Security;

/// <summary>
/// Defines a DLP policy for a specific channel.
/// </summary>
public record DlpPolicy
{
    /// <summary>Gets the channel this policy applies to.</summary>
    public DlpChannel Channel { get; init; }

    /// <summary>Gets the default action when PII is detected.</summary>
    public DlpAction DefaultAction { get; init; } = DlpAction.Audit;

    /// <summary>Gets per-PII-type action overrides.</summary>
    public IReadOnlyDictionary<PiiType, DlpAction> PiiActions { get; init; } = new Dictionary<PiiType, DlpAction>();

    /// <summary>Gets whether this policy is enabled.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// Provides DLP policies for each channel.
/// </summary>
public interface IDlpPolicyProvider
{
    /// <summary>
    /// Gets the DLP policy for the specified channel.
    /// </summary>
    DlpPolicy GetPolicy(DlpChannel channel);

    /// <summary>
    /// Gets all configured DLP policies.
    /// </summary>
    IReadOnlyList<DlpPolicy> GetAllPolicies();
}
