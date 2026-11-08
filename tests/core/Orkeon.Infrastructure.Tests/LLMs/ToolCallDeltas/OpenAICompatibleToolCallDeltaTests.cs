using System.Net;
using System.Text.Json.Nodes;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.LLMs.Base;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.ToolCallDeltas;

/// <summary>
/// FWK-20: the OpenAI dialect streams a tool call in fragments; the stream now hands each fragment to the caller
/// as a <see cref="LlmStreamEventKind.ToolCallDelta"/>, and the terminal event still holds the whole call.
/// </summary>
public sealed class OpenAICompatibleToolCallDeltaTests : IDisposable
{
    private static readonly LlmMessage[] OneUserMessage = [new() { Role = "user", Content = "hi" }];

    private readonly MockHttpClientFactory _httpClientFactory = new();

    private const string Sse =
        """data: {"choices":[{"index":0,"delta":{"role":"assistant","content":null}}]}""" + "\n\n"
        + """data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"directory_read","arguments":""}}]}}]}""" + "\n\n"
        + """data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"path\""}}]}}]}""" + "\n\n"
        + """data: {"choices":[{"index":0,"delta":{"tool_calls":[{"index":0,"function":{"arguments":":\"/src\"}"}},{"index":1,"id":"call_2","type":"function","function":{"name":"file_read","arguments":"{}"}}]}}]}""" + "\n\n"
        + """data: {"choices":[{"index":0,"delta":{},"finish_reason":"tool_calls"}]}""" + "\n\n"
        + "data: [DONE]\n\n";

    [Fact]
    [Trait("BR", "FWK-20")]
    public async Task ShouldEmitOneDeltaPerFragment_InStreamOrder()
    {
        var (events, _) = await StreamAsync(Sse);

        var deltas = events.Where(e => e.Kind == LlmStreamEventKind.ToolCallDelta).Select(e => e.ToolCall!).ToList();
        Assert.Equal(
            [
                new ToolCallDelta(0, "call_1", "directory_read", ""),
                new ToolCallDelta(0, null, null, "{\"path\""),
                new ToolCallDelta(0, null, null, ":\"/src\"}"),
                new ToolCallDelta(1, "call_2", "file_read", "{}"),
            ],
            deltas);
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public async Task ShouldKeepTheFinalToolCallsIdentical_WhateverTheDeltasEmitted()
    {
        var (_, final) = await StreamAsync(Sse);

        var calls = JsonNode.Parse(final.RawResponseBody!)!["choices"]![0]!["message"]!["tool_calls"]!.AsArray();
        Assert.Equal(2, calls.Count);
        Assert.Equal("call_1", calls[0]!["id"]!.GetValue<string>());
        Assert.Equal("directory_read", calls[0]!["function"]!["name"]!.GetValue<string>());
        Assert.Equal("{\"path\":\"/src\"}", calls[0]!["function"]!["arguments"]!.GetValue<string>());
        Assert.Equal("call_2", calls[1]!["id"]!.GetValue<string>());
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public async Task ShouldEmitNoToolCallDelta_WhenTheAnswerIsPlainText()
    {
        var sse = """data: {"choices":[{"index":0,"delta":{"content":"hello"}}]}""" + "\n\n"
            + """data: {"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}""" + "\n\n" + "data: [DONE]\n\n";

        var (events, _) = await StreamAsync(sse);

        Assert.DoesNotContain(events, e => e.Kind == LlmStreamEventKind.ToolCallDelta);
        Assert.Contains(events, e => e.Kind == LlmStreamEventKind.ContentDelta && e.Delta == "hello");
    }

    private async Task<(List<LlmStreamEvent> Events, LlmResponse Final)> StreamAsync(string sse)
    {
        var handler = _httpClientFactory.SetupDefaultHandler();
        handler.SetResponse(HttpStatusCode.OK, sse, "text/event-stream");
        var config = LlmConfig.Create(TestModelName, TestApiKey) with { MaxRetries = 0 };
        using var provider = new TestableOpenAICompatibleProvider(config, _httpClientFactory);

        var events = new List<LlmStreamEvent>();
        await foreach (var e in provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken))
            events.Add(e);

        return (events, Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!);
    }

    public void Dispose()
    {
        _httpClientFactory.Dispose();
        GC.SuppressFinalize(this);
    }
}
