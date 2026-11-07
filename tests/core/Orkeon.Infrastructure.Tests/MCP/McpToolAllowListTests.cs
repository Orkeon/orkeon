using System.Diagnostics.CodeAnalysis;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>FWK-03: Tools:Allow and Tools:Deny filter the tools before they are registered.</summary>
public sealed class McpToolAllowListTests
{
    [SuppressMessage("Reliability", "CA2000", Justification = "Ownership of the transport passes to the client or provider, which the test disposes.")]
    private static async Task<(List<string> Names, McpServerStatus Status)> ConnectAsync(McpServerConfig config)
    {
        var registry = new MockToolRegistry();
        await using var provider = new McpToolProvider(registry);
        await provider.ConnectServerAsync("srv", ToolServer.Create("read", "write", "delete"), config, TestContext.Current.CancellationToken);
        var names = (await registry.GetAllToolsAsync()).Select(t => t.Name).Order().ToList();
        return (names, provider.GetServerStatuses().Single());
    }

    [Fact]
    public async Task ShouldRegisterEverything_WhenAllowIsEmpty()
    {
        var (names, _) = await ConnectAsync(new McpServerConfig());

        Assert.Equal(["delete", "read", "write"], names);
    }

    [Fact]
    public async Task ShouldRegisterOnlyAllowed_WhenAllowIsSet()
    {
        var config = new McpServerConfig();
        config.Tools.Allow.Add("read");

        var (names, _) = await ConnectAsync(config);

        Assert.Equal(["read"], names);
    }

    [Fact]
    public async Task ShouldApplyDenyAfterAllow()
    {
        var config = new McpServerConfig();
        config.Tools.Allow.Add("read");
        config.Tools.Allow.Add("write");
        config.Tools.Deny.Add("write");

        var (names, _) = await ConnectAsync(config);

        Assert.Equal(["read"], names);
    }

    [Fact]
    public async Task ShouldWarn_WhenAnEntryMatchesNoTool()
    {
        var config = new McpServerConfig();
        config.Tools.Allow.Add("read");
        config.Tools.Allow.Add("ghost");
        config.Tools.Deny.Add("phantom");

        var (_, status) = await ConnectAsync(config);

        Assert.Equal(["Allow:ghost", "Deny:phantom"], status.UnmatchedToolFilters);
    }
}
