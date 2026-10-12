using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.LLMs.ToolCalling;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Adapters;

public class LlmProviderToChatClientAdapterToolCallReplayTests
{
    private static readonly ToolSchema s_sampleTool = new(
        Name: "directory_read",
        Description: "Lists files in a directory",
        Parameters: new Dictionary<string, ParameterSchema>
        {
            ["path"] = new ParameterSchema("string", "The directory path", Required: true)
        });

    [Fact]
    [Trait("BR", "CH-04")]
    public async Task ShouldReplayIdTypeAndFunctionOnly_WhenTheToolCallCarriedNothingElse()
    {
        var first = GeminiToolCallBodies.BufferedWithoutExtras("call_1");

        var sent = await RunLoopAsync(first, "call_1");

        var expected = JsonNode.Parse(first)!["choices"]![0]!["message"]!["tool_calls"]!;
        var replayed = ReplayedToolCalls(sent);
        Assert.True(JsonNode.DeepEquals(expected, replayed), replayed.ToJsonString());
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async Task ShouldReplayTheProviderToolCallWhole_WhenAGeminiBufferedAnswerIsFedBack()
    {
        var first = GeminiToolCallBodies.Buffered(("call_1", "sig-A"));

        var sent = await RunLoopAsync(first, "call_1");

        var expected = JsonNode.Parse(first)!["choices"]![0]!["message"]!["tool_calls"]![0]!;
        var replayed = ReplayedToolCalls(sent)[0]!;
        Assert.True(JsonNode.DeepEquals(expected, replayed), replayed.ToJsonString());
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async Task ShouldKeepEachToolCallItsOwnProperties_WhenTwoBufferedCallsAreFedBack()
    {
        var first = GeminiToolCallBodies.Buffered(("call_1", "sig-A"), ("call_2", "sig-B"));

        var sent = await RunLoopAsync(first, "call_1", "call_2");

        var expected = JsonNode.Parse(first)!["choices"]![0]!["message"]!["tool_calls"]!;
        var replayed = ReplayedToolCalls(sent);
        Assert.Equal(2, replayed.Count);
        Assert.True(JsonNode.DeepEquals(expected, replayed), replayed.ToJsonString());
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldReplayTheProviderToolCallWhole_WhenAGeminiStreamedAnswerIsFedBack()
    {
        var streamed = GeminiToolCallBodies.StreamedFragments(("call_1", "sig-A", 0));
        using var handler = new MockHttpMessageHandler();
        var counter = 0;
        handler.SetResponseFactory(_ => counter++ == 0
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(streamed, Encoding.UTF8, "text/event-stream")
            }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(GeminiToolCallBodies.FinalText(), Encoding.UTF8, "application/json")
            });
        using var factory = new MockHttpClientFactory();
        factory.SetDefaultClient(new HttpClient(handler, disposeHandler: false));
        var strategy = new OpenAIToolCallingStrategy(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenAIToolCallParser>.Instance);
        var config = LlmConfig.Create("gemini-3-flash", TestApiKey) with
        {
            BaseUrl = new Uri("https://generativelanguage.googleapis.com/v1beta/openai"),
            Tools = new List<ToolSchema> { s_sampleTool },
            ToolMode = ToolCallMode.Auto,
            TimeoutSeconds = 10
        };
        using var provider = new GeminiLlmProvider(config, factory, strategy);
        using var adapter = new LlmProviderToChatClientAdapter(provider);
        var user = new ChatMessage(ChatRole.User, "Map /src");
        var ct = TestContext.Current.CancellationToken;

        var response = await adapter.GetStreamingResponseAsync([user], cancellationToken: ct)
            .ToChatResponseAsync(ct);
        var history = new List<ChatMessage> { user };
        history.AddRange(response.Messages);
        history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call_1", "ok")]));
        await adapter.GetResponseAsync(history, cancellationToken: ct);

        Assert.Equal(2, handler.AllRequests.Count);
        var expected = JsonNode.Parse("""{"google":{"thought_signature":"sig-A"}}""");
        var replayed = ReplayedToolCalls(handler.LastRequestBody!)[0]!["extra_content"];
        Assert.NotNull(replayed);
        Assert.True(JsonNode.DeepEquals(expected, replayed), replayed.ToJsonString());
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async Task ShouldReplayEachCallItsOwnFunction_WhenTwoBufferedCallsShareAnId()
    {
        var first = GeminiToolCallBodies.BufferedDistinct(
            ("", "directory_read", "{\"path\":\"/src\"}", "sig-A"),
            ("", "directory_read", "{\"path\":\"/docs\"}", "sig-B"));

        var sent = await RunLoopAsync(first, "", "");

        var expected = JsonNode.Parse(first)!["choices"]![0]!["message"]!["tool_calls"]!.AsArray();
        var replayed = ReplayedToolCalls(sent);
        Assert.Equal(2, replayed.Count);
        for (var i = 0; i < 2; i++)
        {
            Assert.True(JsonNode.DeepEquals(expected[i]!["function"], replayed[i]!["function"]),
                replayed[i]!.ToJsonString());
        }
    }

    private static JsonArray ReplayedToolCalls(string secondRequestBody)
    {
        var messages = JsonNode.Parse(secondRequestBody)!["messages"]!.AsArray();
        var assistant = messages.First(m => m!["role"]!.GetValue<string>() == "assistant")!;
        return assistant["tool_calls"]!.AsArray();
    }

    private static async Task<string> RunLoopAsync(string firstAnswer, params string[] callIds)
    {
        using var handler = new MockHttpMessageHandler();
        var bodies = new[] { firstAnswer, GeminiToolCallBodies.FinalText() };
        var counter = 0;
        handler.SetResponseFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(bodies[Math.Min(counter++, bodies.Length - 1)], Encoding.UTF8, "application/json")
        });
        using var factory = new MockHttpClientFactory();
        factory.SetDefaultClient(new HttpClient(handler, disposeHandler: false));

        var strategy = new OpenAIToolCallingStrategy(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenAIToolCallParser>.Instance);
        var config = LlmConfig.Create("gemini-3-flash", TestApiKey) with
        {
            BaseUrl = new Uri("https://generativelanguage.googleapis.com/v1beta/openai"),
            Tools = new List<ToolSchema> { s_sampleTool },
            ToolMode = ToolCallMode.Auto,
            TimeoutSeconds = 10
        };
        using var provider = new GeminiLlmProvider(config, factory, strategy);
        using var adapter = new LlmProviderToChatClientAdapter(provider);

        var user = new ChatMessage(ChatRole.User, "Map /src");
        var ct = TestContext.Current.CancellationToken;
        var response = await adapter.GetResponseAsync([user], cancellationToken: ct);

        var history = new List<ChatMessage> { user };
        history.AddRange(response.Messages);
        history.Add(new ChatMessage(ChatRole.Tool,
            callIds.Select(id => (AIContent)new FunctionResultContent(id, "ok")).ToList()));
        await adapter.GetResponseAsync(history, cancellationToken: ct);

        Assert.Equal(2, handler.AllRequests.Count);
        return handler.LastRequestBody!;
    }
}
