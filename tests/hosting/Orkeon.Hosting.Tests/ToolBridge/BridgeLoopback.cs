using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Tools;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Hosting.ToolBridge;
using Orkeon.Infrastructure.Tools;
using Orkeon.Tests.Shared.Network;

namespace Orkeon.Hosting.Tests.ToolBridge;

/// <summary>Both sides of the tool bridge in one process: a server on a free loopback port, a caller's link to it.</summary>
internal static class BridgeLoopback
{
    /// <summary>The token both sides read.</summary>
    public const string Token = "test-bridge-token";

    /// <summary>Reads <see cref="Token"/> under the default variable, nothing else.</summary>
    public static string? Environment(string name) => name == ToolBridgeProtocol.DefaultTokenEnvVar ? Token : null;

    /// <summary>Starts a server over <paramref name="tools"/>, serving <paramref name="allow"/>.</summary>
    public static async Task<(ToolBridgeServer Server, Uri Address)> StartServerAsync(
        IEnumerable<IBaseTool> tools, string[] allow, FakeAuditLogger? audit = null, long? maxRequestBytes = null, int? port = null)
    {
        var registry = new ToolRegistry(tools);
        ToolBridgeServer Create(int p)
        {
            var options = new ToolBridgeServerOptions { Prefix = LoopbackPorts.BaseAddress(p).ToString() };
            foreach (var name in allow)
                options.Allow.Add(name);
            if (maxRequestBytes is { } max)
                options.MaxRequestBytes = max;
            return new ToolBridgeServer(registry, options, Environment, audit, NullLogger.Instance);
        }

        var ct = TestContext.Current.CancellationToken;
        var (server, chosen) = port is { } fixedPort
            ? await LoopbackPorts.StartAsync(Create, (s, c) => s.StartAsync(c), () => fixedPort, ct)
            : await LoopbackPorts.StartAsync(Create, (s, c) => s.StartAsync(c), ct);
        return (server, LoopbackPorts.BaseAddress(chosen));
    }

    /// <summary>The caller's options for the bridge at <paramref name="address"/>.</summary>
    public static ToolBridgeClientOptions ClientOptions(Uri address, params string[] allow)
    {
        var options = new ToolBridgeClientOptions { Address = address.ToString() };
        foreach (var name in allow)
            options.Allow.Add(name);
        return options;
    }

    /// <summary>Connects a link of <paramref name="options"/> over <paramref name="registry"/>.</summary>
    public static async Task<ToolBridgeLink> ConnectAsync(ToolRegistry registry, ToolBridgeClientOptions options, string? token = Token)
    {
        var link = new ToolBridgeLink(options, name => name == ToolBridgeProtocol.DefaultTokenEnvVar ? token : null);
        await link.ConnectAsync(registry, null, null, NullLogger.Instance, NullLogger.Instance, TestContext.Current.CancellationToken);
        return link;
    }
}
