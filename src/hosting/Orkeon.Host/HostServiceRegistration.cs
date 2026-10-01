using Microsoft.Extensions.DependencyInjection;

namespace Orkeon.Host;

/// <summary>
/// The daemon's hosted services, in the one order that is correct — kept out of
/// <c>Program.cs</c> so a test can hold the order.
/// </summary>
internal static class HostServiceRegistration
{
    /// <summary>
    /// Registers the hosted services. They start in this order and stop in the reverse one:
    /// <list type="number">
    /// <item><see cref="McpConnectionService"/> first, so the MCP servers' tools are in the
    /// registry before the channel can deliver a message that loads a crew, and so the servers
    /// stay connected until every run has drained (GAP-11);</item>
    /// <item>the chat channel before the crew host, so it stops after it — the drain must run
    /// while the channel can still deliver, or the grace period keeps runs alive to produce
    /// answers nobody can receive;</item>
    /// <item><see cref="CrewHostService"/>, whose stop is the drain.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddHostLifetimeServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHostedService<McpConnectionService>();
        services.AddHostedService<Gateway.ChatChannelService>();
        services.AddHostedService<CrewHostService>();
        return services;
    }
}
