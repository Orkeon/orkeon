using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.CallMode;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.CallMode;

/// <summary>
/// Under Auto, a clear refusal of streaming before any event falls back to a buffered call for the
/// rest of the run, warned once; anything else propagates; Stream mode never falls back.
/// </summary>
public sealed class CallModeFallbackTests
{
    private static readonly LlmMessage[] Conversation = [LlmMessage.User("hello")];

    private static MockStreamingLlmProvider Inner(string name = "vendor-x")
    {
        var inner = new MockStreamingLlmProvider { StreamsNatively = true, Name = name };
        inner.SetStreamingChunks(["streamed"]);
        inner.SetChatResult("buffered answer");
        return inner;
    }

    private static CallModeLlmProvider Decorate(MockStreamingLlmProvider inner, LlmCallMode? mode = null, ILogger? logger = null) =>
        new(inner, mode ?? LlmCallMode.Auto, logger ?? NullLogger.Instance);

    [Theory]
    [InlineData(404, "")]
    [InlineData(405, "")]
    [InlineData(415, "")]
    [InlineData(501, "")]
    [InlineData(400, "stream is not supported")]
    [InlineData(422, "Streaming not allowed")]
    [InlineData(0, "")] // sentinel: the provider declares no streaming at all
    [Trait("BR", "BR-06")]
    public async Task ShouldFallBackToBuffer_WhenStreamingIsRefusedBeforeAnyChunk(int status, string body)
    {
        var inner = Inner();
        if (status == 0)
            inner.SupportsStreaming = false;
        else
            inner.RefuseStreamingWith((HttpStatusCode)status, body);
        var provider = Decorate(inner);
        using var run = LlmCallModeScope.Open();

        var response = await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("buffered answer", response.Content);
        Assert.Equal(1, inner.ChatCallCount);
        Assert.Equal(LlmWireMode.BufferFallback, provider.LastWireMode);
    }

    [Fact]
    [Trait("BR", "BR-06")]
    public async Task ShouldStayInBufferForTheRestOfTheRun_AfterAFallback()
    {
        var inner = Inner();
        inner.RefuseStreamingWith(HttpStatusCode.NotFound);
        var provider = Decorate(inner);
        using var run = LlmCallModeScope.Open();

        await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);
        await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, inner.ChatStreamingCallCount);
        Assert.Equal(2, inner.ChatCallCount);
    }

    [Fact]
    [Trait("BR", "BR-06")]
    public async Task ShouldTryStreamingAgain_WhenTheNextRunStarts()
    {
        var inner = Inner();
        inner.RefuseStreamingWith(HttpStatusCode.NotFound);
        var provider = Decorate(inner);
        using (LlmCallModeScope.Open())
            await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);
        inner.LiftRefusal();

        using var next = LlmCallModeScope.Open();
        await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(LlmWireMode.Stream, provider.LastWireMode);
        Assert.Equal(2, inner.ChatStreamingCallCount);
    }

    [Fact]
    [Trait("BR", "BR-06")]
    [Trait("BR", "BR-13")]
    public async Task ShouldKeepStreaming_OnAnotherSettingOfTheSameVendor_AfterAFallback()
    {
        var refusing = Inner("same-vendor");
        refusing.RefuseStreamingWith(HttpStatusCode.NotFound);
        var healthy = Inner("same-vendor");
        var first = Decorate(refusing);
        var second = Decorate(healthy);
        using var run = LlmCallModeScope.Open();

        await first.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);
        await second.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(LlmWireMode.BufferFallback, first.LastWireMode);
        Assert.Equal(LlmWireMode.Stream, second.LastWireMode);
        Assert.Equal(0, healthy.ChatCallCount);
    }

    [Fact]
    [Trait("BR", "BR-07")]
    public async Task ShouldWarnOncePerRun_WhenFallingBack()
    {
        var inner = Inner("vendor-x");
        inner.RefuseStreamingWith(HttpStatusCode.NotFound);
        var logger = new RecordingLogger();
        var provider = Decorate(inner, logger: logger);
        using var run = LlmCallModeScope.Open();

        await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);
        await provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken);

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("vendor-x", warning.Message);
        Assert.Contains("Buffer", warning.Message);
    }

    public static TheoryData<Exception> NotAboutStreaming() => new()
    {
        new HttpRequestException("unauthorized", null, HttpStatusCode.Unauthorized),
        new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden),
        new HttpRequestException("slow down", null, HttpStatusCode.TooManyRequests),
        new HttpRequestException("invalid temperature: only 1 is allowed", null, HttpStatusCode.BadRequest),
        new TaskCanceledException("timed out", new TimeoutException()),
    };

    [Theory]
    [MemberData(nameof(NotAboutStreaming))]
    [Trait("BR", "BR-06")]
    public async Task ShouldFailWithoutASecondCall_WhenTheRefusalIsNotAboutStreaming(Exception failure)
    {
        var inner = Inner();
        inner.FailStreamingWith(failure);
        var provider = Decorate(inner);
        using var run = LlmCallModeScope.Open();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
            provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken));

        Assert.IsType(failure.GetType(), thrown);
        Assert.Equal(0, inner.ChatCallCount);
    }

    [Fact]
    [Trait("BR", "BR-06")]
    public async Task ShouldFailWithoutFallback_WhenTheStreamBreaksAfterAChunk()
    {
        var inner = Inner();
        inner.SetChatStreamingHandler((_, _, _) => BreakAfterOneChunk());
        var provider = Decorate(inner);
        using var run = LlmCallModeScope.Open();

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(0, inner.ChatCallCount);
    }

    [Fact]
    [Trait("BR", "BR-06")]
    public async Task ShouldFailNamingAutoAndBuffer_WhenStreamModeIsRefused()
    {
        var inner = Inner();
        inner.RefuseStreamingWith(HttpStatusCode.NotFound);
        var provider = Decorate(inner, LlmCallMode.Stream);
        using var run = LlmCallModeScope.Open();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
            provider.ChatAsync(Conversation, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Auto", thrown.Message);
        Assert.Contains("Buffer", thrown.Message);
        Assert.Equal(0, inner.ChatCallCount);
    }

    private static async IAsyncEnumerable<LlmStreamEvent> BreakAfterOneChunk()
    {
        yield return LlmStreamEvent.Content("first");
        await Task.Yield();
        throw new HttpRequestException("cut", null, HttpStatusCode.NotFound);
    }
}
