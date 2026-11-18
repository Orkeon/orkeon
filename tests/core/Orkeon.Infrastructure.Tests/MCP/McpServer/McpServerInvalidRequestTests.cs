using System.Text.Json;
using Orkeon.Application.Services.Security;
using Orkeon.Infrastructure.MCP;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.MCP;

/// <summary>
/// #414: a line that is JSON but no valid request is answered <c>-32600 Invalid Request</c> - never
/// <c>-32700</c> (kept for text that is not JSON), <c>-32601</c>, a success or silence. The answer carries the
/// <c>id</c> when it could be read (a string or an integer), <c>null</c> otherwise.
/// </summary>
public sealed class McpServerInvalidRequestTests
{
    private const int InvalidRequest = -32600;

    private static McpServer Server() =>
        new(new MockToolRegistry(), ToolInvocationPipeline.Unguarded, new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService(), new McpServerOptions());

    /// <summary>What the stdio loop writes for the lines it is fed, once it has read them all.</summary>
    private static async Task<List<JsonElement>> AnswersAsync(params string[] lines)
    {
        using var reader = new StringReader(string.Join('\n', lines) + "\n");
        using var writer = new StringWriter();

        await Server().RunStdioAsync(reader, writer, TestContext.Current.CancellationToken);

        return writer.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonElement.Parse(line))
            .ToList();
    }

    private static void AssertRefused(JsonElement answer, string expectedId)
    {
        Assert.False(answer.TryGetProperty("result", out _), answer.GetRawText());
        Assert.Equal(InvalidRequest, answer.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(expectedId, answer.GetProperty("id").GetRawText());
    }

    // --- Through the stdio loop: one test per line of the issue's table ---

    [Theory]
    // Valid JSON that is no request object.
    [InlineData("null", "null")]
    [InlineData("5", "null")]
    [InlineData("\"str\"", "null")]
    // Batching is not spoken: one answer for the whole line.
    [InlineData("[]", "null")]
    [InlineData("""[{"jsonrpc":"2.0","id":3,"method":"ping"}]""", "null")]
    [InlineData("""[{"jsonrpc":"2.0","id":3,"method":"ping"},{"jsonrpc":"2.0","id":4,"method":"ping"}]""", "null")]
    // method is required, and a string.
    [InlineData("""{"jsonrpc":"2.0","id":14}""", "14")]
    [InlineData("""{"jsonrpc":"2.0","id":15,"method":5}""", "15")]
    [InlineData("""{"jsonrpc":"2.0","id":"a","method":""}""", "\"a\"")]
    // jsonrpc must be exactly "2.0".
    [InlineData("""{"jsonrpc":"1.0","id":17,"method":"ping"}""", "17")]
    [InlineData("""{"id":18,"method":"ping"}""", "18")]
    [InlineData("""{"jsonrpc":2.0,"id":18,"method":"ping"}""", "18")]
    // params must be an array or an object.
    [InlineData("""{"jsonrpc":"2.0","id":16,"method":"ping","params":"x"}""", "16")]
    [InlineData("""{"jsonrpc":"2.0","id":16,"method":"ping","params":5}""", "16")]
    // MCP: the id must not be null, and is a string or an integer.
    [InlineData("""{"jsonrpc":"2.0","id":null,"method":"ping"}""", "null")]
    [InlineData("""{"jsonrpc":"2.0","id":1.5,"method":"ping"}""", "null")]
    [InlineData("""{"jsonrpc":"2.0","id":{"a":1},"method":"ping"}""", "null")]
    [InlineData("""{"jsonrpc":"2.0","id":true,"method":"ping"}""", "null")]
    // MCP: a notification must not include an id.
    [InlineData("""{"jsonrpc":"2.0","id":19,"method":"notifications/foo"}""", "19")]
    [InlineData("""{"jsonrpc":"2.0","id":20,"method":"notifications/cancelled","params":{"requestId":1}}""", "20")]
    public async Task ShouldAnswerInvalidRequest_WhenTheLineIsNoValidRequest(string line, string expectedId)
    {
        var answers = await AnswersAsync(line);

        var answer = Assert.Single(answers);
        AssertRefused(answer, expectedId);
    }

    [Theory]
    [InlineData("this is not JSON")]
    [InlineData("""{"jsonrpc":"2.0","id":1,"method":"ping" """)]
    public async Task ShouldKeepTheParseError_WhenTheLineIsNotJson(string line)
    {
        var answer = Assert.Single(await AnswersAsync(line));

        Assert.Equal(-32700, answer.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("id").ValueKind);
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","method":"notifications/initialized"}""")]
    [InlineData("""{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":99}}""")]
    [InlineData("""{"jsonrpc":"2.0","method":"ping"}""")]
    public async Task ShouldStayHushed_WhenTheLineIsAWellFormedNotification(string line)
    {
        Assert.Empty(await AnswersAsync(line));
    }

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":7,"method":"ping"}""", "7")]
    [InlineData("""{"jsonrpc":"2.0","id":"seven","method":"ping"}""", "\"seven\"")]
    [InlineData("""{"jsonrpc":"2.0","id":8,"method":"ping","params":null}""", "8")]
    [InlineData("""{"jsonrpc":"2.0","id":9,"method":"ping","params":{}}""", "9")]
    [InlineData("""{"jsonrpc":"2.0","id":10,"method":"ping","params":[]}""", "10")]
    public async Task ShouldStillAnswer_WhenTheRequestIsValid(string line, string expectedId)
    {
        var answer = Assert.Single(await AnswersAsync(line));

        Assert.True(answer.TryGetProperty("result", out _), answer.GetRawText());
        Assert.Equal(expectedId, answer.GetProperty("id").GetRawText());
    }

    [Fact]
    public async Task ShouldAnswerEveryLine_WhenValidAndInvalidLinesAlternate()
    {
        var answers = await AnswersAsync(
            "null",
            """{"jsonrpc":"2.0","id":1,"method":"ping"}""",
            "[]",
            """{"jsonrpc":"2.0","id":2,"method":"ping"}""");

        Assert.Equal(4, answers.Count);
        Assert.Equal(2, answers.Count(a => a.TryGetProperty("error", out _)));
        Assert.Equal(2, answers.Count(a => a.TryGetProperty("result", out _)));
    }

    // --- Through ProcessRequestAsync, a host's entry point: the rows it can receive ---

    private static JsonRpcRequest Request(string json) => JsonSerializer.Deserialize<JsonRpcRequest>(json)!;

    [Theory]
    [InlineData("""{"jsonrpc":"2.0","id":1.5,"method":"ping"}""", "null")]
    [InlineData("""{"jsonrpc":"2.0","id":{"a":1},"method":"ping"}""", "null")]
    [InlineData("""{"jsonrpc":"2.0","id":true,"method":"ping"}""", "null")]
    [InlineData("""{"jsonrpc":"2.0","id":14,"method":""}""", "14")]
    [InlineData("""{"jsonrpc":"2.0","id":14}""", "14")]
    [InlineData("""{"jsonrpc":"1.0","id":17,"method":"ping"}""", "17")]
    [InlineData("""{"jsonrpc":"2.0","id":16,"method":"ping","params":"x"}""", "16")]
    [InlineData("""{"jsonrpc":"2.0","id":19,"method":"notifications/foo"}""", "19")]
    public async Task ShouldAnswerInvalidRequest_WhenAHostHandsOverNoValidRequest(string json, string expectedId)
    {
        var response = await Server().ProcessRequestAsync(Request(json), TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        AssertRefused(JsonSerializer.SerializeToElement(response), expectedId);
    }

    /// <summary>
    /// A host that keeps the null it read hands over a Null-kind id, and is answered. One that deserializes the
    /// message into a <see cref="JsonRpcRequest"/> gets no id at all - a notification -, which is why the stdio
    /// loop reads the null id from the text.
    /// </summary>
    [Fact]
    public async Task ShouldAnswerInvalidRequest_WhenAHostHandsOverANullId()
    {
        var request = new JsonRpcRequest { Method = "ping", Id = JsonElement.Parse("null") };

        var response = await Server().ProcessRequestAsync(request, TestContext.Current.CancellationToken);

        Assert.NotNull(response);
        AssertRefused(JsonSerializer.SerializeToElement(response), "null");
    }

    [Fact]
    public async Task ShouldReturnNoAnswer_WhenAHostHandsOverANotification()
    {
        var response = await Server().ProcessRequestAsync(
            Request("""{"jsonrpc":"2.0","method":"notifications/initialized"}"""), TestContext.Current.CancellationToken);

        Assert.Null(response);
    }
}
