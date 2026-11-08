using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.ToolCalling;
using Orkeon.Infrastructure.Tests.TestDoubles;

namespace Orkeon.Infrastructure.Tests.LLMs.ToolCallDeltas;

/// <summary>FWK-20: Ollama emits a tool call whole in one frame, so each call is one complete tool-call delta.</summary>
public sealed class OllamaToolCallDeltaTests
{
    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<OllamaLlmProvider> _logger = new();

    private const string Frames =
        """{"model":"llama3.2","message":{"role":"assistant","content":""},"done":false}"""
        + "\n"
        + """{"model":"llama3.2","message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"get_weather","arguments":{"city":"Paris"}}},{"function":{"name":"get_time","arguments":{}}}]},"done":false}"""
        + "\n"
        + """{"model":"llama3.2","message":{"role":"assistant","content":""},"done":true,"prompt_eval_count":5,"eval_count":3}""";

    [Fact]
    [Trait("BR", "FWK-20")]
    public async Task ShouldEmitOneCompleteDeltaPerToolCall_IndexedInOrder()
    {
        var events = await CollectAsync();

        var deltas = events.Where(e => e.Kind == LlmStreamEventKind.ToolCallDelta).Select(e => e.ToolCall!).ToList();
        Assert.Equal(2, deltas.Count);
        Assert.Equal(0, deltas[0].Index);
        Assert.Equal("get_weather", deltas[0].Name);
        Assert.Equal("""{"city":"Paris"}""", deltas[0].ArgumentsDelta);
        Assert.Equal(1, deltas[1].Index);
        Assert.Equal("get_time", deltas[1].Name);
        Assert.Equal("{}", deltas[1].ArgumentsDelta);
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public async Task ShouldKeepTheFinalToolCallsIdentical_WhateverTheDeltasEmitted()
    {
        var events = await CollectAsync();

        var final = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.Contains("get_weather", final.RawResponseBody);
        Assert.Contains("get_time", final.RawResponseBody);
    }

    private async Task<List<LlmStreamEvent>> CollectAsync()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, Frames);
        _httpClientFactory.RegisterClient("OllamaLlmProvider", new HttpClient(handler));
        var config = LlmConfig.Create("llama3.2") with
        {
            BaseUrl = new Uri("http://localhost:11434"),
            Tools = [new ToolSchema("get_weather", "Get the weather", [])],
        };
        using var provider = new OllamaLlmProvider(
            config, _httpClientFactory, new OpenAIToolCallingStrategy(NullLogger<OpenAIToolCallParser>.Instance), _logger);

        var ct = TestContext.Current.CancellationToken;
        var events = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync([LlmMessage.User("weather?")], config, ct))
            events.Add(ev);
        return events;
    }
}
