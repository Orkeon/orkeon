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
    public async Task ShouldWriteNoNullMember_WhenServingPromptsCrewToolsProgressAndModernRequests()
    {
        // A prompt and an argument with no description, a prompt answer with none, a crew tool that reports
        // progress, and requests that declare their protocol version per request (modern lineage).
        var extension = new FakeMcpServerExtension { ProgressSteps = 2 };
        extension.PromptList.Clear();
        extension.PromptList.Add(new McpPrompt { Name = "greet", Arguments = [new McpPromptArgument { Name = "who" }] });
        var registry = new MockToolRegistry();
        registry.AddTool("search", new InlineMockBaseTool("search", "Search",
            new Dictionary<string, ParameterSchema> { ["query"] = new("string", "The query", true) },
            new ToolCallResponse(true, "found", null)));
        var server = new McpServer(registry, ToolInvocationPipeline.Unguarded, new FakeRightsFileSystemService(),
            new McpServerOptions { Name = "TestServer", Version = "1.0.0" }, null, extension);
        const string modern = $$"""{"{{McpProtocol.MetaProtocolVersion}}":"{{McpProtocol.ModernVersion}}"}""";
        string[] session =
        [
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"0"}}}""",
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
            """{"jsonrpc":"2.0","id":2,"method":"prompts/list"}""",
            """{"jsonrpc":"2.0","id":3,"method":"prompts/get","params":{"name":"greet","arguments":{"who":"x"}}}""",
            """{"jsonrpc":"2.0","id":4,"method":"prompts/get","params":{"name":"nope"}}""",
            """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"run_crew_demo","arguments":{},"_meta":{"progressToken":"tok-1"}}}""",
            """{"jsonrpc":"2.0","id":6,"method":"tools/list","params":{"_meta":""" + modern + "}}",
            """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"search","arguments":{"query":"x"},"_meta":""" + modern + "}}",
            """{"jsonrpc":"2.0","id":8,"method":"ping","params":{"_meta":""" + modern + "}}",
        ];
        using var reader = new StringReader(string.Join('\n', session) + "\n");
        using var writer = new StringWriter();

        await server.RunStdioAsync(reader, writer, TestContext.Current.CancellationToken);

        var lines = writer.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonElement.Parse(line))
            .ToList();
        var notifications = lines.Where(l => l.TryGetProperty("method", out _)).ToList();
        var answers = lines.Where(l => !l.TryGetProperty("method", out _)).ToList();
        // Every request is answered, and the crew call's two steps went out as progress notifications.
        Assert.Equal(session.Length - 1, answers.Count);
        Assert.Equal(2, notifications.Count);
        Assert.All(notifications, n => Assert.Equal("notifications/progress", n.GetProperty("method").GetString()));
        foreach (var line in lines)
        {
            var raw = line.GetRawText();
            if (!line.TryGetProperty("method", out _))
            {
                Assert.True(line.TryGetProperty("result", out _) ^ line.TryGetProperty("error", out _), raw);
                Assert.NotEqual(JsonValueKind.Null, line.GetProperty("id").ValueKind);
            }

            foreach (var member in line.EnumerateObject())
                Assert.Empty(NullMembers(member.Value, member.Name));
        }

        // The prompt without a description is listed, and its answer read, without a null in either.
        var listed = Answer(answers, 2).GetProperty("result").GetProperty("prompts")[0];
        Assert.False(listed.TryGetProperty("description", out _));
        Assert.False(listed.GetProperty("arguments")[0].TryGetProperty("description", out _));
        Assert.False(Answer(answers, 3).GetProperty("result").TryGetProperty("description", out _));
        // The errors are among the lines checked: the unknown prompt, the ping the modern lineage dropped.
        Assert.True(Answer(answers, 4).TryGetProperty("error", out _));
        Assert.True(Answer(answers, 8).TryGetProperty("error", out _));
        Assert.True(Answer(answers, 5).TryGetProperty("result", out _));
        Assert.True(Answer(answers, 7).TryGetProperty("result", out _));
    }

    private static JsonElement Answer(List<JsonElement> answers, int id) =>
        answers.Single(a => a.GetProperty("id").ValueKind == JsonValueKind.Number && a.GetProperty("id").GetInt32() == id);

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
