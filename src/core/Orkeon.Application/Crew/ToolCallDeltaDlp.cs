using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Application.Crew;

/// <summary>
/// What a host that enforces DLP hands the orchestrator so the tool calls a model builds are not streamed in
/// pieces: the arguments of each call are held until the call ends, scanned once and written as one delta.
/// </summary>
/// <param name="Interceptor">The interceptor of the external-output channel.</param>
/// <param name="Policy">The policy it applies.</param>
public sealed record ToolCallDeltaDlp(IDlpInterceptor Interceptor, DlpPolicy Policy)
{
    /// <summary>
    /// The external-output interceptor and its policy the host registered (<c>AddOrkeonDlp</c>), or null when it
    /// registered no DLP or disabled the channel.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <returns>The DLP to hold tool-call fragments for, or null to stream them as they arrive.</returns>
    public static ToolCallDeltaDlp? From(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var interceptor = services.GetServices<IDlpInterceptor>().FirstOrDefault(i => i.Channel == DlpChannel.ExternalOutput);
        var policies = services.GetService<IDlpPolicyProvider>();
        if (interceptor is null || policies is null)
            return null;

        var policy = policies.GetPolicy(DlpChannel.ExternalOutput);
        return policy.Enabled ? new ToolCallDeltaDlp(interceptor, policy) : null;
    }
}
