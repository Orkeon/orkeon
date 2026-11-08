using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Fallback;

/// <summary>FWK-05: a stream moves to the next profile before its first delta, and never after.</summary>
public sealed class FallbackStreamingTests
{
    private static readonly LlmMessage[] Hello = [new() { Role = "user", Content = "hi" }];

    private static FallbackLlmProvider Chain(FakeLlmFallbackObserver observer, FakeScriptedLlmProvider a, FakeScriptedLlmProvider b) =>
        new("main", [new FallbackLink("a", a), new FallbackLink("b", b)], observer, NullLogger.Instance, null, TimeProvider.System);

    private static async Task<List<LlmStreamEvent>> Drain(FallbackLlmProvider chain)
    {
        var events = new List<LlmStreamEvent>();
        await foreach (var ev in chain.ChatStreamingAsync(Hello, cancellationToken: TestContext.Current.CancellationToken))
            events.Add(ev);
        return events;
    }

    private static LlmStreamEvent[] Answer(string text) =>
        [LlmStreamEvent.Content(text), LlmStreamEvent.Complete(new LlmResponse { Content = text })];

    [Fact]
    public async Task A_stream_that_throws_before_its_first_delta_moves_to_the_next_profile()
    {
        var a = new FakeScriptedLlmProvider { StreamThrowsBefore = new HttpRequestException("down", null, HttpStatusCode.ServiceUnavailable) };
        var b = new FakeScriptedLlmProvider { StreamEvents = Answer("from-b") };
        var observer = new FakeLlmFallbackObserver();

        var events = await Drain(Chain(observer, a, b));

        Assert.Equal("from-b", events[0].Delta);
        Assert.Equal("from-b", events[^1].FinalResponse!.Content);
        Assert.Equal(("main", "a", "b", "server_error"), Assert.Single(observer.Taken));
    }

    [Fact]
    public async Task A_stream_that_ends_on_a_failed_completion_without_a_delta_moves_on()
    {
        var a = new FakeScriptedLlmProvider
        {
            StreamEvents = [LlmStreamEvent.Complete(FakeScriptedLlmProvider.Http(HttpStatusCode.TooManyRequests))],
        };
        var b = new FakeScriptedLlmProvider { StreamEvents = Answer("from-b") };
        var observer = new FakeLlmFallbackObserver();

        var events = await Drain(Chain(observer, a, b));

        Assert.Equal(2, events.Count);
        Assert.Equal("from-b", events[0].Delta);
        Assert.Equal("rate_limited", Assert.Single(observer.Taken).Reason);
    }

    [Fact]
    public async Task A_stream_that_fails_after_its_first_delta_is_not_moved()
    {
        var a = new FakeScriptedLlmProvider
        {
            StreamEvents = [LlmStreamEvent.Content("partial")],
            StreamThrowsAfter = new HttpRequestException("cut", null, HttpStatusCode.BadGateway),
        };
        var b = new FakeScriptedLlmProvider { StreamEvents = Answer("from-b") };
        var observer = new FakeLlmFallbackObserver();
        var seen = new List<string?>();

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var ev in Chain(observer, a, b).ChatStreamingAsync(Hello, cancellationToken: TestContext.Current.CancellationToken))
                seen.Add(ev.Delta);
        });

        Assert.Equal(["partial"], seen);
        Assert.Equal(0, b.Calls);
        Assert.Empty(observer.Taken);
    }

    [Fact]
    public async Task A_failed_completion_after_a_delta_is_passed_through()
    {
        var failure = FakeScriptedLlmProvider.Http(HttpStatusCode.InternalServerError);
        var a = new FakeScriptedLlmProvider { StreamEvents = [LlmStreamEvent.Content("partial"), LlmStreamEvent.Complete(failure)] };
        var b = new FakeScriptedLlmProvider { StreamEvents = Answer("from-b") };
        var observer = new FakeLlmFallbackObserver();

        var events = await Drain(Chain(observer, a, b));

        Assert.Same(failure, events[^1].FinalResponse);
        Assert.Equal(0, b.Calls);
        Assert.Empty(observer.Taken);
    }

    [Fact]
    public async Task A_refused_stream_is_not_moved()
    {
        var a = new FakeScriptedLlmProvider { StreamThrowsBefore = new HttpRequestException("no", null, HttpStatusCode.BadRequest) };
        var b = new FakeScriptedLlmProvider { StreamEvents = Answer("from-b") };

        await Assert.ThrowsAsync<HttpRequestException>(() => Drain(Chain(new FakeLlmFallbackObserver(), a, b)));

        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public async Task A_chain_whose_every_stream_fails_before_a_delta_throws_one_error_naming_them()
    {
        var a = new FakeScriptedLlmProvider { StreamThrowsBefore = new HttpRequestException("a down") };
        var b = new FakeScriptedLlmProvider { StreamThrowsBefore = new HttpRequestException("b down") };

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => Drain(Chain(new FakeLlmFallbackObserver(), a, b)));

        Assert.Contains("a down", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("b down", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Text_streams_follow_the_same_rule()
    {
        var a = new FakeScriptedLlmProvider { StreamThrowsBefore = new HttpRequestException("down", null, HttpStatusCode.ServiceUnavailable) };
        var b = new FakeScriptedLlmProvider { Chunks = ["one", "two"] };
        var observer = new FakeLlmFallbackObserver();
        var chunks = new List<string>();

        await foreach (var chunk in Chain(observer, a, b).GenerateStreamingAsync("hi", cancellationToken: TestContext.Current.CancellationToken))
            chunks.Add(chunk);

        Assert.Equal(["one", "two"], chunks);
        Assert.Single(observer.Taken);

        var c = new FakeScriptedLlmProvider { Chunks = ["x"], StreamThrowsAfter = new HttpRequestException("cut", null, HttpStatusCode.BadGateway) };
        var d = new FakeScriptedLlmProvider { Chunks = ["y"] };
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in Chain(observer, c, d).GenerateStreamingAsync("hi", cancellationToken: TestContext.Current.CancellationToken)) { }
        });
        Assert.Equal(0, d.Calls);
    }

    [Fact]
    public async Task A_cancellation_reported_by_a_completion_is_not_failed_over()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var a = new FakeScriptedLlmProvider
        {
            StreamEvents = [LlmStreamEvent.Complete(FakeScriptedLlmProvider.Failed("The request was canceled", "TaskCanceledException"))],
        };
        var b = new FakeScriptedLlmProvider { StreamEvents = Answer("from-b") };
        var observer = new FakeLlmFallbackObserver();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Chain(observer, a, b).ChatStreamingAsync(Hello, cancellationToken: cancelled.Token)) { }
        });

        Assert.Equal(0, b.Calls);
        Assert.Empty(observer.Taken);
    }
}
