using System.Net;
using System.Text;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Polly;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.ToolCallDeltas;

/// <summary>FWK-20: a streamed <c>tool_use</c> block reaches the caller as tool-call deltas, its input as <c>input_json_delta</c> pieces.</summary>
public sealed class AnthropicToolCallDeltaTests
{
    private readonly TestHttpClientFactory _httpClientFactory = new();
    private readonly TestLogger<AnthropicLlmProvider> _logger = new();

    private const string Sse =
        """data: {"type":"message_start","message":{"usage":{"input_tokens":12}}}""" + "\n\n"
        + """data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""" + "\n\n"
        + """data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Let me look."}}""" + "\n\n"
        + """data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_1","name":"directory_read","input":{}}}""" + "\n\n"
        + """data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"path\""}}""" + "\n\n"
        + """data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":":\"/src\"}"}}""" + "\n\n"
        + """data: {"type":"message_stop"}""" + "\n\n";

    [Fact]
    [Trait("BR", "FWK-20")]
    public async Task ShouldEmitTheOpeningThenOneDeltaPerInputFragment()
    {
        var events = await CollectAsync();

        var deltas = events.Where(e => e.Kind == LlmStreamEventKind.ToolCallDelta).Select(e => e.ToolCall!).ToList();
        Assert.Equal(
            [
                new ToolCallDelta(1, "toolu_1", "directory_read", ""),
                new ToolCallDelta(1, null, null, "{\"path\""),
                new ToolCallDelta(1, null, null, ":\"/src\"}"),
            ],
            deltas);
    }

    [Fact]
    [Trait("BR", "FWK-20")]
    public async Task ShouldKeepTheFinalToolCallIdentical_WhateverTheDeltasEmitted()
    {
        var events = await CollectAsync();

        var final = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.Contains("toolu_1", final.RawResponseBody);
        Assert.Contains("directory_read", final.RawResponseBody);
        Assert.Contains("/src", final.RawResponseBody);
        Assert.Equal("Let me look.", final.Content);
    }

    private async Task<List<LlmStreamEvent>> CollectAsync()
    {
        using var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(Sse))),
        });
        _httpClientFactory.RegisterClient("AnthropicLlmProvider", new HttpClient(handler));
        var config = LlmConfig.Create("claude-sonnet-5", TestApiKey) with { MaxRetries = 0 };
        using var provider = new AnthropicLlmProvider(config, _httpClientFactory, Policy.NoOpAsync<HttpResponseMessage>(), _logger);

        var events = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync([LlmMessage.User("hi")], cancellationToken: TestContext.Current.CancellationToken))
            events.Add(ev);
        return events;
    }
}
