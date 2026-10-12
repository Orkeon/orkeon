using System.Net;
using System.Text.Json.Nodes;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Base;

/// <summary>
/// A streamed tool call keeps every property its fragments carried beyond index, id, type and function
/// (Gemini's <c>extra_content</c>), each index its own, in the synthesised <c>RawResponseBody</c>.
/// </summary>
public sealed class OpenAICompatibleProviderBaseStreamedToolCallExtrasTests : IDisposable
{
    private static readonly LlmMessage[] OneUserMessage = [new() { Role = "user", Content = "hi" }];

    private readonly MockHttpClientFactory _httpClientFactory = new();

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldKeepTheFragmentProperties_WhenAStreamedToolCallCarriesExtraContent()
    {
        var calls = await StreamAsync(GeminiToolCallBodies.StreamedFragments(("call_1", "sig-A", 0)));

        var call = Assert.Single(calls)!;
        AssertSignature("sig-A", call);
        Assert.Equal("call_1", call["id"]!.GetValue<string>());
        Assert.Equal("function", call["type"]!.GetValue<string>());
        Assert.Equal("directory_read", call["function"]!["name"]!.GetValue<string>());
        Assert.Equal("{\"path\":\"/src\"}", call["function"]!["arguments"]!.GetValue<string>());
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldKeepTheFragmentProperties_WhenTheExtraContentArrivesOnALaterFragment()
    {
        var calls = await StreamAsync(GeminiToolCallBodies.StreamedFragments(("call_1", "sig-A", 2)));

        var call = Assert.Single(calls)!;
        AssertSignature("sig-A", call);
        Assert.Equal("{\"path\":\"/src\"}", call["function"]!["arguments"]!.GetValue<string>());
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async Task ShouldKeepEachToolCallItsOwnProperties_WhenTwoStreamedCallsCarryTheirOwn()
    {
        var calls = await StreamAsync(
            GeminiToolCallBodies.StreamedFragments(("call_1", "sig-A", 0), ("call_2", "sig-B", 2)));

        Assert.Equal(2, calls.Count);
        Assert.Equal("call_1", calls[0]!["id"]!.GetValue<string>());
        AssertSignature("sig-A", calls[0]!);
        Assert.Equal("call_2", calls[1]!["id"]!.GetValue<string>());
        AssertSignature("sig-B", calls[1]!);
    }

    private static void AssertSignature(string signature, JsonNode call)
    {
        var expected = JsonNode.Parse($$$"""{"google":{"thought_signature":"{{{signature}}}"}}""");
        var actual = call["extra_content"];
        Assert.NotNull(actual);
        Assert.True(JsonNode.DeepEquals(expected, actual), actual.ToJsonString());
    }

    private async Task<JsonArray> StreamAsync(string sse)
    {
        var handler = _httpClientFactory.SetupDefaultHandler();
        handler.SetResponse(HttpStatusCode.OK, sse, "text/event-stream");
        var config = LlmConfig.Create(TestModelName, TestApiKey) with { MaxRetries = 0 };
        using var provider = new TestableOpenAICompatibleProvider(config, _httpClientFactory);

        var events = new List<LlmStreamEvent>();
        await foreach (var e in provider.ChatStreamingAsync(OneUserMessage, cancellationToken: TestContext.Current.CancellationToken))
            events.Add(e);

        var completed = Assert.Single(events, e => e.Kind == LlmStreamEventKind.Completed).FinalResponse!;
        Assert.NotNull(completed.RawResponseBody);
        return JsonNode.Parse(completed.RawResponseBody)!["choices"]![0]!["message"]!["tool_calls"]!.AsArray();
    }

    public void Dispose()
    {
        _httpClientFactory.Dispose();
        GC.SuppressFinalize(this);
    }
}
