using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.CallMode;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.CallMode;

/// <summary>
/// The decorator that decides the wire mode of every call: a buffered caller is streamed on the
/// wire under Auto/Stream and gets the folded response; Buffer and providers without a native
/// stream stay buffered; a streamed call is metered once; each profile carries its own mode.
/// </summary>
public sealed class CallModeLlmProviderTests
{
    private static readonly LlmMessage[] Conversation = [LlmMessage.User("hello")];

    private static LlmResponse Completed() => new()
    {
        Content = "streamed answer",
        TokensUsed = 10,
        PromptTokens = 7,
        CompletionTokens = 3,
        Model = "vendor/model-x",
    };

    private static MockStreamingLlmProvider StreamingProvider()
    {
        var inner = new MockStreamingLlmProvider { StreamsNatively = true };
        inner.SetStreamingChunks(["streamed ", "answer"]);
        inner.SetCompletedResponse(Completed());
        inner.SetChatResult(new LlmResponse { Content = "buffered answer", TokensUsed = 4, PromptTokens = 3, CompletionTokens = 1 });
        return inner;
    }

    [Fact]
    [Trait("BR", "BR-05")]
    [Trait("BR", "BR-13")]
    public async Task ShouldStreamOnTheWire_WhenABufferedCallerUsesAnAutoSetting()
    {
        var inner = StreamingProvider();
        var provider = new CallModeLlmProvider(inner, LlmCallMode.Auto, NullLogger.Instance);

        var response = await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("streamed answer", response.Content);
        Assert.Equal(7, response.PromptTokens);
        Assert.Equal(3, response.CompletionTokens);
        Assert.Equal(1, inner.ChatStreamingCallCount);
        Assert.Equal(0, inner.ChatCallCount);
        Assert.Equal(LlmWireMode.Stream, provider.LastWireMode);
    }

    [Fact]
    [Trait("BR", "BR-13")]
    public async Task ShouldCallBuffered_WhenTheModeIsBuffer()
    {
        var inner = StreamingProvider();
        var provider = new CallModeLlmProvider(inner, LlmCallMode.Buffer, NullLogger.Instance);
        var ct = TestContext.Current.CancellationToken;

        var response = await provider.ChatAsync(Conversation, cancellationToken: ct);

        Assert.Equal("buffered answer", response.Content);
        Assert.Equal(1, inner.ChatCallCount);
        Assert.Equal(0, inner.ChatStreamingCallCount);
        Assert.Equal(LlmWireMode.Buffer, provider.LastWireMode);

        var events = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync(Conversation, null, ct).WithCancellation(ct))
            events.Add(ev);

        Assert.Equal(2, events.Count);
        Assert.Equal(LlmStreamEventKind.ContentDelta, events[0].Kind);
        Assert.Equal("buffered answer", events[0].Delta);
        Assert.Equal(LlmStreamEventKind.Completed, events[1].Kind);
        Assert.Equal("buffered answer", events[1].FinalResponse!.Content);
        Assert.Equal(0, inner.ChatStreamingCallCount);
        Assert.Equal(LlmWireMode.Buffer, provider.LastWireMode);
    }

    [Fact]
    [Trait("BR", "BR-13")]
    public async Task ShouldReportBuffer_WhenTheProviderHasNoNativeStream()
    {
        var inner = StreamingProvider();
        inner.SupportsStreaming = true;
        inner.StreamsNatively = false;
        var provider = new CallModeLlmProvider(inner, LlmCallMode.Auto, NullLogger.Instance);

        var response = await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("buffered answer", response.Content);
        Assert.Equal(1, inner.ChatCallCount);
        Assert.Equal(0, inner.ChatStreamingCallCount);
        Assert.Equal(LlmWireMode.Buffer, provider.LastWireMode);
    }

    [Fact]
    [Trait("BR", "BR-11")]
    public async Task ShouldThrowTheRefusal_WhenABufferedTextStreamIsRefused()
    {
        var inner = StreamingProvider();
        inner.SetGenerateResult(new LlmResponse
        {
            Content = "",
            Metadata = new Dictionary<string, object> { ["error"] = "quota exhausted for this key" },
        });
        var provider = new CallModeLlmProvider(inner, LlmCallMode.Buffer, NullLogger.Instance);
        var ct = TestContext.Current.CancellationToken;

        var ex = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in provider.GenerateStreamingAsync("hello", null, ct).WithCancellation(ct))
            {
            }
        });

        Assert.Contains("quota exhausted for this key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-11")]
    public async Task ShouldYieldNoEmptyDelta_WhenABufferedChatAnswerIsEmpty()
    {
        var inner = StreamingProvider();
        inner.SetChatResult(new LlmResponse { Content = "" });
        var provider = new CallModeLlmProvider(inner, LlmCallMode.Buffer, NullLogger.Instance);
        var ct = TestContext.Current.CancellationToken;

        var events = new List<LlmStreamEvent>();
        await foreach (var ev in provider.ChatStreamingAsync(Conversation, null, ct).WithCancellation(ct))
            events.Add(ev);

        Assert.DoesNotContain(events, e => e.Kind == LlmStreamEventKind.ContentDelta);
        var completed = Assert.Single(events);
        Assert.Equal(LlmStreamEventKind.Completed, completed.Kind);
    }

    [Fact]
    [Trait("BR", "BR-11")]
    public async Task ShouldMeterAStreamedCallOnce()
    {
        var inner = StreamingProvider();
        var sink = new MockLlmUsageSink();
        var metered = MeteredLlmProvider.Wrap(new CallModeLlmProvider(inner, LlmCallMode.Auto, NullLogger.Instance), sink);

        await metered.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        var usage = Assert.Single(sink.Recorded);
        Assert.Equal(7, usage.PromptTokens);
        Assert.Equal(3, usage.CompletionTokens);
        Assert.Equal(1, inner.ChatStreamingCallCount);
    }

    [Fact]
    [Trait("BR", "BR-13")]
    public async Task ShouldGiveEachProfileItsOwnMode_WhenTwoProfilesRun()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Llm:Profiles:fast:BaseUrl"] = "https://api.anthropic.com/v1",
                ["Llm:Profiles:fast:ApiKey"] = "sk-ant",
                ["Llm:Profiles:fast:Model"] = "claude-sonnet-5",
                ["Llm:Profiles:fast:CallMode"] = "Buffer",
                ["Llm:Profiles:live:BaseUrl"] = "https://api.anthropic.com/v1",
                ["Llm:Profiles:live:ApiKey"] = "sk-ant",
                ["Llm:Profiles:live:Model"] = "claude-sonnet-5",
                ["Llm:Profiles:live:CallMode"] = "Stream",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();
        services.AddOrkeonLlmProfiles(configuration);
        await using var container = services.BuildServiceProvider();
        var registry = container.GetRequiredService<ILlmProfileRegistry>();

        var fast = CallModeLlmProvider.Of(registry.Resolve("fast").Provider);
        var live = CallModeLlmProvider.Of(registry.Resolve("live").Provider);

        Assert.Equal(LlmCallMode.Buffer, fast!.Mode);
        Assert.Equal(LlmCallMode.Stream, live!.Mode);
    }
}
