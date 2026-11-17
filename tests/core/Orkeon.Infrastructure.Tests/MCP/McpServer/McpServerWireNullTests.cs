using System.Text;
using System.Text.Json;
using Orkeon.Application.Services.Security;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// #405: what the server writes on the wire holds no null member. A JSON-RPC response carries
/// <c>result</c> or <c>error</c>, never both, and an optional member the server leaves out is absent,
/// not <c>null</c>: a strict client (the MCP SDKs) refuses the message otherwise. The one null that
/// stays is the <c>id</c> of the answer to a line that could not be parsed.
/// </summary>
public sealed class McpServerWireNullTests
{
    private static readonly string[] Session =
    [
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"0"}}}""",
        """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
        """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
        """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"search","arguments":{"query":"x"}}}""",
        """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"nope","arguments":{}}}""",
        """{"jsonrpc":"2.0","id":5,"method":"nope"}""",
        """{"jsonrpc":"2.0","id":6,"method":"ping"}""",
        """{"jsonrpc":"2.0","id":7,"method":"resources/list"}""",
        """{"jsonrpc":"2.0","id":8,"method":"resources/read","params":{"uri":"orkeon:///output/report.md"}}""",
        """{"jsonrpc":"2.0","id":9,"method":"resources/read","params":{"uri":"orkeon:///output/missing.md"}}""",
        """{"jsonrpc":"2.0","id":10,"method":"tools/call","params":{"name":"failing","arguments":{}}}""",
        """{"jsonrpc":"2.0","id":11,"method":"server/discover"}""",
        "this is not JSON",
    ];

    [Fact]
    [Trait("BR", "#405")]
    public async Task ShouldWriteNoNullMember_WhenAnsweringOverStdio()
    {
        var registry = new MockToolRegistry();
        registry.AddTool("search", new InlineMockBaseTool("search", "Search",
            new Dictionary<string, ParameterSchema> { ["query"] = new("string", "The query", true) },
            new ToolCallResponse(true, "found", null)));
        registry.AddTool("failing", new InlineMockBaseTool("failing", "Fails",
            callResult: new ToolCallResponse(false, null, "it failed")));
        var files = new FakeRightsFileSystemService()
            .AddMount("/output", FileAccessRights.ReadOnly)
            .AddFile("/output/report.md", Encoding.UTF8.GetBytes("# report"));
        var server = new McpServer(registry, ToolInvocationPipeline.Unguarded, files,
            new McpServerOptions { Name = "TestServer", Version = "1.0.0", ExposeOutput = true });
        using var reader = new StringReader(string.Join('\n', Session) + "\n");
        using var writer = new StringWriter();

        // The run ends with the input, once every request is answered.
        await server.RunStdioAsync(reader, writer, TestContext.Current.CancellationToken);

        var answers = writer.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonElement.Parse(line))
            .ToList();
        // Every line but the notification is answered.
        Assert.Equal(Session.Length - 1, answers.Count);
        Assert.Single(answers, a => a.GetProperty("id").ValueKind == JsonValueKind.Null);
        foreach (var answer in answers)
        {
            var raw = answer.GetRawText();
            Assert.True(answer.TryGetProperty("result", out _) ^ answer.TryGetProperty("error", out _), raw);
            foreach (var member in answer.EnumerateObject().Where(m => m.Name != "id"))
                Assert.Empty(NullMembers(member.Value, member.Name));
            Assert.NotEqual(JsonValueKind.Undefined, answer.GetProperty("id").ValueKind);
        }

        // The failures are among them: an error answer and a failed tool call are checked like the others.
        Assert.Equal(4, answers.Count(a => a.TryGetProperty("error", out _)));
        Assert.Contains(answers, a => a.TryGetProperty("result", out var r) && r.TryGetProperty("isError", out var e) && e.GetBoolean());
    }

    [Fact]
    [Trait("BR", "#405")]
    public void ShouldLeaveOutWhatItDoesNotCarry_WhenAClientMessageIsWritten()
    {
        var request = JsonSerializer.Serialize(new JsonRpcRequest { Method = "tools/list", Id = JsonSerializer.SerializeToElement(1) });
        var notification = JsonSerializer.Serialize(new JsonRpcNotification { Method = "notifications/initialized" });
        var call = JsonSerializer.Serialize(new McpToolCallParams { Name = "search" });

        Assert.Equal("""{"jsonrpc":"2.0","method":"tools/list","id":1}""", request);
        Assert.Equal("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", notification);
        Assert.Equal("""{"name":"search"}""", call);
    }

    [Fact]
    [Trait("BR", "#405")]
    public void ShouldKeepANullId_WhenTheRequestCouldNotBeRead()
    {
        var answer = JsonSerializer.Serialize(new JsonRpcResponse { Error = new JsonRpcError(JsonRpcErrorCodes.ParseError, "Parse error") });

        Assert.Equal("""{"jsonrpc":"2.0","error":{"code":-32700,"message":"Parse error"},"id":null}""", answer);
    }

    /// <summary>The paths of the null members under <paramref name="value"/>, in document order.</summary>
    private static List<string> NullMembers(JsonElement value, string path)
    {
        var found = new List<string>();
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                found.Add(path);
                break;
            case JsonValueKind.Object:
                foreach (var member in value.EnumerateObject())
                    found.AddRange(NullMembers(member.Value, $"{path}.{member.Name}"));
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in value.EnumerateArray())
                    found.AddRange(NullMembers(item, $"{path}[{index++}]"));
                break;
        }

        return found;
    }
}
