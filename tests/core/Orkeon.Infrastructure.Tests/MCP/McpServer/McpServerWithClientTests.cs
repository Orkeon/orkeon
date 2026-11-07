using System.Text;
using Orkeon.Application.Services.Security;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// FWK-18: the repository's own <see cref="McpClient"/> against the server's stdio loop, both real:
/// the pair works when the client's calls overlap, and the client's cursor loop follows the
/// pages the server issues.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The client owns the transport and the test disposes the client.")]
public sealed class McpServerWithClientTests
{
    [Fact]
    public async Task ShouldCompleteTwoOverlappingCalls_WhenTheClientCallsTheServerOverStdio()
    {
        var tool = new MockGateTool("barrier", releaseWhenEntered: 2);
        var registry = new MockToolRegistry();
        registry.AddTool(tool.Name, tool);
        var server = new McpServer(registry, ToolInvocationPipeline.Unguarded, new FakeFileSystemService(), new McpServerOptions());
        await using var client = new McpClient(new FakeStdioMcpTransport(server));
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        var both = await Task.WhenAll(
            client.CallToolAsync("barrier", null, TestContext.Current.CancellationToken),
            client.CallToolAsync("barrier", null, TestContext.Current.CancellationToken))
            .WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);

        Assert.All(both, result => Assert.False(result.IsError));
        Assert.Equal(2, tool.MaxInFlight);
    }

    [Fact]
    public async Task ShouldFollowTheCursorsOfTheServer_WhenTheClientListsResources()
    {
        var fs = new FakeRightsFileSystemService().AddMount("/output", FileAccessRights.ReadOnly);
        for (var i = 1; i <= 5; i++)
            fs.AddFile($"/output/f{i}.txt", Encoding.UTF8.GetBytes("x"));
        var server = new McpServer(
            new MockToolRegistry(), ToolInvocationPipeline.Unguarded,
            fs, new McpServerOptions { ExposeOutput = true, ResourcesPageSize = 2 });
        await using var client = new McpClient(new FakeStdioMcpTransport(server));
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        var resources = await client.ListResourcesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            Enumerable.Range(1, 5).Select(i => $"orkeon:///output/f{i}.txt"),
            resources.Select(r => r.Uri!.ToString()));

        var read = await client.ReadResourceAsync(resources[0].Uri!, TestContext.Current.CancellationToken);

        var contents = Assert.Single(read.Contents);
        Assert.Equal("x", contents.Text);
        Assert.Equal(resources[0].Uri, contents.Uri);
    }
}
