using Orkeon.Application.Interfaces.Security;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Security.Dlp;

/// <summary>
/// Provides DLP policies from configuration options.
/// </summary>
public sealed class DlpPolicyProvider : IDlpPolicyProvider
{
    private readonly DlpOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DlpPolicyProvider"/> class.
    /// </summary>
    /// <param name="options">The DLP configuration options.</param>
    public DlpPolicyProvider(IOptions<DlpOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public DlpPolicy GetPolicy(DlpChannel channel)
    {
        if (_options.ChannelPolicies.TryGetValue(channel, out var policy))
            return policy;

        return new DlpPolicy
        {
            Channel = channel,
            DefaultAction = _options.DefaultAction,
            Enabled = _options.Enabled
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<DlpPolicy> GetAllPolicies()
    {
        var policies = new List<DlpPolicy>();
        foreach (DlpChannel channel in Enum.GetValues<DlpChannel>())
        {
            policies.Add(GetPolicy(channel));
        }
        return policies;
    }
}
