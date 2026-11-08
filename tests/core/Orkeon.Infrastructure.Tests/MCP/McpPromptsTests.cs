using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>FWK-10: prompts/list, prompts/get and the built-in mcp_prompt_get tool.</summary>
public sealed class McpPromptsTests
{
    private static JsonRpcResponse Ok(JsonRpcRequest request, object result) =>
        new() { Id = request.Id, Result = JsonSerializer.SerializeToElement(result, result.GetType()) };

    [SuppressMessage("Reliability", "CA2000", Justification = "Ownership of the transport passes to the client or provider, which the test disposes.")]
    private static MockMcpTransport PromptServer()
    {
        var transport = new MockMcpTransport();
        transport.SetConnected(true);
        transport.SetSendRequestFunc(request => request.Method switch
        {
            "prompts/list" => Ok(request, new
            {
                prompts = new[]
                {
                    new { name = "review", description = "Review code", arguments = new[] { new { name = "lang", required = true } } },
                },
            }),
            "prompts/get" => Ok(request, new
            {
                description = "Review code",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new { type = "text", text = "Review this " + request.Params!.Value.GetProperty("arguments").GetProperty("lang").GetString() },
                    },
                },
            }),
            _ => ToolServer.Respond(request, []),
        });
        return transport;
    }

    [Fact]
    public async Task ShouldListPrompts_WithTheirArguments()
    {
        await using var client = new McpClient(PromptServer());
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        var prompts = await client.ListPromptsAsync(TestContext.Current.CancellationToken);

        var prompt = Assert.Single(prompts);
        Assert.Equal("review", prompt.Name);
        Assert.Equal("lang", Assert.Single(prompt.Arguments).Name);
        Assert.True(prompt.Arguments[0].Required);
    }

    [Fact]
    public async Task ShouldGetAPrompt_WithItsArguments()
    {
        var transport = PromptServer();
        await using var client = new McpClient(transport);
        await client.ConnectAsync(TestContext.Current.CancellationToken);

        var result = await client.GetPromptAsync(
            "review", new Dictionary<string, string> { ["lang"] = "C#" }, TestContext.Current.CancellationToken);

        var message = Assert.Single(result.Messages);
        Assert.Equal("user", message.Role);
        Assert.Equal("Review this C#", message.Content.Text);
        var sent = transport.AllSentRequests.Single(r => r.Method == "prompts/get").Params!.Value;
        Assert.Equal("review", sent.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ShouldReturnThePromptText_FromTheMcpPromptGetTool()
    {
        await using var provider = new McpToolProvider(new MockToolRegistry());
        await provider.ConnectServerAsync("srv", PromptServer(), new McpServerConfig(), TestContext.Current.CancellationToken);
        using var tool = new McpPromptGetTool(provider);

        var response = await tool.CallAsync(
            new ToolCallRequest("mcp_prompt_get", new Dictionary<string, object?>
            {
                ["server"] = "srv",
                ["name"] = "review",
                ["arguments"] = new Dictionary<string, string> { ["lang"] = "Go" },
            }),
            TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        Assert.Contains("Review this Go", System.Text.Json.JsonSerializer.Serialize(response.Result), StringComparison.Ordinal);
        Assert.Equal("mcp_prompt_get", tool.Name);
    }

    [Fact]
    public async Task ShouldListThePrompts_WhenTheMcpPromptGetToolGetsNoName()
    {
        await using var provider = new McpToolProvider(new MockToolRegistry());
        await provider.ConnectServerAsync("srv", PromptServer(), new McpServerConfig(), TestContext.Current.CancellationToken);
        using var tool = new McpPromptGetTool(provider);

        var response = await tool.CallAsync(
            new ToolCallRequest("mcp_prompt_get", new Dictionary<string, object?> { ["server"] = "srv" }),
            TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        var json = System.Text.Json.JsonSerializer.Serialize(response.Result);
        Assert.Contains("review", json, StringComparison.Ordinal);
        Assert.Contains("lang (required)", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldFail_WhenTheServerIsNotConnected()
    {
        await using var provider = new McpToolProvider(new MockToolRegistry());
        using var tool = new McpPromptGetTool(provider);

        var response = await tool.CallAsync(
            new ToolCallRequest("mcp_prompt_get", new Dictionary<string, object?> { ["server"] = "ghost", ["name"] = "x" }),
            TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Contains("ghost", response.Error, StringComparison.Ordinal);
    }
}
