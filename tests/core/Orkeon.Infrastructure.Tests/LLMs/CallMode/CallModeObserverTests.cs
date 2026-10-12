using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.CallMode;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.CallMode;

/// <summary>Every call through the call-mode decorator is announced, followed and closed.</summary>
public sealed class CallModeObserverTests
{
    private static readonly LlmMessage[] Question = [new LlmMessage { Role = "user", Content = "one two three four" }];

    private static (CallModeLlmProvider Provider, RecordingLlmCallObserver Observer) Build(
        MockStreamingLlmProvider inner, LlmCallMode mode)
    {
        var observer = new RecordingLlmCallObserver();
        return (new CallModeLlmProvider(inner, mode, NullLogger.Instance, observer, new FakeWordTokenCounter()), observer);
    }

    [Fact]
    [Trait("BR", "BR-08")]
    [Trait("BR", "BR-09")]
    public async Task ShouldAnnounceTheCallWithItsEstimatedPromptAndMode_WhenSent()
    {
        var inner = new MockStreamingLlmProvider { StreamsNatively = true };
        var (streamed, streamedObserver) = Build(inner, LlmCallMode.Auto);
        var (buffered, bufferedObserver) = Build(new MockStreamingLlmProvider(), LlmCallMode.Buffer);

        await streamed.ChatAsync(Question, null, TestContext.Current.CancellationToken);
        await buffered.ChatAsync(Question, null, TestContext.Current.CancellationToken);

        var onWire = Assert.Single(streamedObserver.StartedCalls);
        Assert.False(string.IsNullOrWhiteSpace(onWire.CallId));
        Assert.Equal(LlmCallMode.Stream, onWire.Mode);
        Assert.Equal(4, onWire.EstimatedPromptTokens);
        var inBuffer = Assert.Single(bufferedObserver.StartedCalls);
        Assert.Equal(LlmCallMode.Buffer, inBuffer.Mode);
        Assert.Equal(4, inBuffer.EstimatedPromptTokens);
        Assert.Empty(bufferedObserver.ProgressCalls);
    }

    [Fact]
    [Trait("BR", "BR-08")]
    public async Task ShouldReportReceivedTokensAsChunksArrive_WhenStreamed()
    {
        var inner = new MockStreamingLlmProvider { StreamsNatively = true };
        inner.SetStreamingChunks(["a b ", "c d e ", "f"]);
        var (provider, observer) = Build(inner, LlmCallMode.Auto);

        await provider.ChatAsync(Question, null, TestContext.Current.CancellationToken);

        Assert.Equal([2, 5, 6], observer.ProgressCalls.Select(p => p.EstimatedCompletionTokens));
        Assert.All(observer.ProgressCalls, p => Assert.Equal(observer.StartedCalls[0].CallId, p.CallId));
    }

    [Fact]
    [Trait("BR", "BR-08")]
    public async Task ShouldCountEachDeltaOnce_WhenStreamed()
    {
        string[] deltas = ["a b ", "c d e ", "f"];
        var inner = new MockStreamingLlmProvider { StreamsNatively = true };
        inner.SetStreamingChunks(deltas);
        var observer = new RecordingLlmCallObserver();
        var counter = new FakeWordTokenCounter();
        var provider = new CallModeLlmProvider(inner, LlmCallMode.Auto, NullLogger.Instance, observer, counter);

        await provider.ChatAsync(Question, null, TestContext.Current.CancellationToken);

        var promptCharacters = Question[0].Content!.Length;
        Assert.Equal(deltas.Sum(d => d.Length), counter.CharactersCounted - promptCharacters);
        Assert.Equal([2, 5, 6], observer.ProgressCalls.Select(p => p.EstimatedCompletionTokens));
    }

    [Fact]
    [Trait("BR", "BR-07")]
    [Trait("BR", "BR-08")]
    public async Task ShouldCloseTheCallWithTheProvidersCountAndFallback()
    {
        var inner = new MockStreamingLlmProvider { StreamsNatively = true };
        inner.RefuseStreamingWith(HttpStatusCode.NotFound);
        inner.SetChatResult(new LlmResponse { Content = "buffered answer", PromptTokens = 11, CompletionTokens = 7 });
        var (provider, observer) = Build(inner, LlmCallMode.Auto);

        await provider.ChatAsync(Question, null, TestContext.Current.CancellationToken);

        var started = Assert.Single(observer.StartedCalls);
        var finished = Assert.Single(observer.FinishedCalls);
        Assert.Equal(started.CallId, finished.CallId);
        Assert.Equal(LlmCallMode.Buffer, finished.Mode);
        Assert.True(finished.FellBack);
        Assert.Equal(11, finished.PromptTokens);
        Assert.Equal(7, finished.CompletionTokens);
    }
}
