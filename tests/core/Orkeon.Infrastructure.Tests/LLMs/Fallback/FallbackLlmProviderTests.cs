using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Profiles;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.LLMs.Fallback;

/// <summary>FWK-05: a profile's chain moves a call that failed in a way a retry cannot cure, and only that one.</summary>
public sealed class FallbackLlmProviderTests
{
    private static readonly LlmMessage[] Hello = [new() { Role = "user", Content = "hi" }];

    private static FallbackLlmProvider Chain(
        FakeLlmFallbackObserver observer,
        TimeSpan? budget = null,
        TimeProvider? time = null,
        params (string Name, ILlmProvider Provider)[] links) =>
        new("main", [.. links.Select(l => new FallbackLink(l.Name, l.Provider))], observer, NullLogger.Instance, budget, time ?? TimeProvider.System);

    [Fact]
    public async Task A_503_moves_the_call_to_the_next_profile_and_says_so()
    {
        var a = new FakeScriptedLlmProvider { Response = FakeScriptedLlmProvider.Http(HttpStatusCode.ServiceUnavailable) };
        var b = new FakeScriptedLlmProvider { Response = new LlmResponse { Content = "from-b" } };
        var observer = new FakeLlmFallbackObserver();
        var chain = Chain(observer, links: [("a", a), ("b", b)]);

        var answer = await chain.ChatAsync(Hello, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("from-b", answer.Content);
        Assert.Equal(1, a.Calls);
        Assert.Equal(1, b.Calls);
        var taken = Assert.Single(observer.Taken);
        Assert.Equal(("main", "a", "b", "server_error"), taken);
    }

    [Fact]
    [Trait("BR", "CH-328")]
    public async Task A_429_for_an_exhausted_balance_moves_the_call_to_the_next_profile()
    {
        var a = new FakeScriptedLlmProvider
        {
            Response = FakeScriptedLlmProvider.Failed(
                "Z.AI API error: TooManyRequests - {\"error\":{\"code\":\"insufficient_quota\"}}" + LlmFailureClassifier.BalanceExhaustedNote,
                "APIError"),
        };
        var b = new FakeScriptedLlmProvider { Response = new LlmResponse { Content = "from-b" } };
        var observer = new FakeLlmFallbackObserver();
        var chain = Chain(observer, links: [("a", a), ("b", b)]);

        var answer = await chain.ChatAsync(Hello, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("from-b", answer.Content);
        Assert.Equal(1, a.Calls);
        Assert.Equal(1, b.Calls);
        Assert.Equal(("main", "a", "b", "rate_limited"), Assert.Single(observer.Taken));
    }

    [Fact]
    public async Task Generate_follows_the_same_rule()
    {
        var a = new FakeScriptedLlmProvider { Response = FakeScriptedLlmProvider.Http(HttpStatusCode.BadGateway) };
        var b = new FakeScriptedLlmProvider { Response = new LlmResponse { Content = "from-b" } };
        var chain = Chain(new FakeLlmFallbackObserver(), links: [("a", a), ("b", b)]);

        var answer = await chain.GenerateAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("from-b", answer.Content);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task A_refusal_of_the_request_stays_the_answer_and_nothing_else_is_tried(HttpStatusCode status)
    {
        var refusal = FakeScriptedLlmProvider.Http(status);
        var a = new FakeScriptedLlmProvider { Response = refusal };
        var b = new FakeScriptedLlmProvider();
        var observer = new FakeLlmFallbackObserver();
        var chain = Chain(observer, links: [("a", a), ("b", b)]);

        var answer = await chain.ChatAsync(Hello, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Same(refusal, answer);
        Assert.Equal(0, b.Calls);
        Assert.Empty(observer.Taken);
    }

    [Fact]
    public async Task A_blocked_content_and_an_unclassed_failure_do_not_move_the_call()
    {
        var blocked = FakeScriptedLlmProvider.Failed("Blocked by the decision stage", "DecisionBlocked");
        var unclassed = FakeScriptedLlmProvider.Failed("No line matches the prompt", "");
        var observer = new FakeLlmFallbackObserver();
        var b = new FakeScriptedLlmProvider();

        foreach (var failure in new[] { blocked, unclassed })
        {
            var a = new FakeScriptedLlmProvider { Response = failure };
            var answer = await Chain(observer, links: [("a", a), ("b", b)]).ChatAsync(Hello, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Same(failure, answer);
        }

        Assert.Equal(0, b.Calls);
        Assert.Empty(observer.Taken);
    }

    [Theory]
    [InlineData("rate_limited", "Fake API error: TooManyRequests - slow down", "APIError")]
    [InlineData("timeout", "Fake API error: RequestTimeout - late", "APIError")]
    [InlineData("server_error", "Fake API error: 529 - overloaded", "APIError")]
    [InlineData("timeout", "Fake did not answer within Llm:TimeoutSeconds = 5 s", "TaskCanceledException")]
    [InlineData("timeout", "stream went quiet", "StreamIdleTimeout")]
    [InlineData("transport", "Fake API call failed: connection refused", "HttpRequestException")]
    [InlineData("transport", "stream closed before any answer", "StreamTruncated")]
    [InlineData("exhausted", "every profile failed", "FallbackExhausted")]
    public async Task Each_failure_a_retry_cannot_cure_moves_the_call(string reason, string error, string errorType)
    {
        var a = new FakeScriptedLlmProvider { Response = FakeScriptedLlmProvider.Failed(error, errorType) };
        var b = new FakeScriptedLlmProvider { Response = new LlmResponse { Content = "from-b" } };
        var observer = new FakeLlmFallbackObserver();

        var answer = await Chain(observer, links: [("a", a), ("b", b)]).ChatAsync(Hello, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("from-b", answer.Content);
        Assert.Equal(reason, Assert.Single(observer.Taken).Reason);
    }

    [Fact]
    public async Task A_thrown_transport_error_or_a_5xx_exception_moves_the_call()
    {
        var observer = new FakeLlmFallbackObserver();
        var b = new FakeScriptedLlmProvider { Response = new LlmResponse { Content = "from-b" } };

        foreach (var thrown in new Exception[]
                 {
                     new HttpRequestException("connection refused"),
                     new HttpRequestException("bad gateway", null, HttpStatusCode.BadGateway),
                     new TaskCanceledException("timed out", new TimeoutException()),
                 })
        {
            var a = new FakeScriptedLlmProvider { Throws = thrown };
            var answer = await Chain(observer, links: [("a", a), ("b", b)]).ChatAsync(Hello, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("from-b", answer.Content);
        }

        Assert.Equal(3, observer.Taken.Count);
    }

    [Fact]
    public async Task A_thrown_400_and_an_unrelated_exception_reach_the_caller()
    {
        var b = new FakeScriptedLlmProvider();
        var observer = new FakeLlmFallbackObserver();

        var badRequest = new FakeScriptedLlmProvider { Throws = new HttpRequestException("no", null, HttpStatusCode.BadRequest) };
        await Assert.ThrowsAsync<HttpRequestException>(() => Chain(observer, links: [("a", badRequest), ("b", b)])
            .ChatAsync(Hello, cancellationToken: TestContext.Current.CancellationToken));

        var unrelated = new FakeScriptedLlmProvider { Throws = new InvalidOperationException("bug") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Chain(observer, links: [("a", unrelated), ("b", b)])
            .ChatAsync(Hello, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(0, b.Calls);
        Assert.Empty(observer.Taken);
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_not_a_failure_to_fail_over()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var a = new FakeScriptedLlmProvider { Throws = new TaskCanceledException("cancelled", new TimeoutException()) };
        var b = new FakeScriptedLlmProvider();

        await Assert.ThrowsAsync<TaskCanceledException>(() => Chain(new FakeLlmFallbackObserver(), links: [("a", a), ("b", b)])
            .ChatAsync(Hello, cancellationToken: cancelled.Token));

        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public async Task A_chain_walks_every_profile_in_order_and_reports_each_hop()
    {
        var a = new FakeScriptedLlmProvider { Response = FakeScriptedLlmProvider.Http(HttpStatusCode.InternalServerError) };
        var b = new FakeScriptedLlmProvider { Response = FakeScriptedLlmProvider.Http(HttpStatusCode.TooManyRequests) };
        var c = new FakeScriptedLlmProvider { Response = new LlmResponse { Content = "from-c" } };
        var observer = new FakeLlmFallbackObserver();

        var answer = await Chain(observer, links: [("a", a), ("b", b), ("c", c)]).ChatAsync(Hello, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("from-c", answer.Content);
        Assert.Equal(
            [("main", "a", "b", "server_error"), ("main", "b", "c", "rate_limited")],
            observer.Taken);
    }

    [Fact]
    public async Task An_exhausted_chain_answers_with_one_error_that_names_every_profile()
    {
        var a = new FakeScriptedLlmProvider { Response = FakeScriptedLlmProvider.Http(HttpStatusCode.ServiceUnavailable) };
        var b = new FakeScriptedLlmProvider { Response = FakeScriptedLlmProvider.Failed("Fake did not answer", "TaskCanceledException") };
        var observer = new FakeLlmFallbackObserver();

        var answer = await Chain(observer, links: [("a", a), ("b", b)]).ChatAsync(Hello, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("FallbackExhausted", answer.ErrorType);
        Assert.NotNull(answer.Error);
        Assert.Contains("main", answer.Error, StringComparison.Ordinal);
        Assert.Contains("a:", answer.Error, StringComparison.Ordinal);
        Assert.Contains("ServiceUnavailable", answer.Error, StringComparison.Ordinal);
        Assert.Contains("b:", answer.Error, StringComparison.Ordinal);
        Assert.Contains("did not answer", answer.Error, StringComparison.Ordinal);
        Assert.Single(observer.Taken);
    }

    [Fact]
    public async Task An_exhausted_chain_of_exceptions_throws_once_with_the_last_as_its_cause()
    {
        var a = new FakeScriptedLlmProvider { Throws = new HttpRequestException("a down") };
        var last = new HttpRequestException("b down");
        var b = new FakeScriptedLlmProvider { Throws = last };

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => Chain(new FakeLlmFallbackObserver(), links: [("a", a), ("b", b)])
            .ChatAsync(Hello, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(last, thrown.InnerException);
        Assert.Contains("a down", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("b down", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_chains_time_budget_stops_the_walk_once_it_is_spent()
    {
        var clock = new ManualTimeProvider();
        var a = new FakeScriptedLlmProvider
        {
            Response = FakeScriptedLlmProvider.Http(HttpStatusCode.ServiceUnavailable),
            OnCall = () => clock.Advance(TimeSpan.FromSeconds(31)),
        };
        var b = new FakeScriptedLlmProvider();
        var observer = new FakeLlmFallbackObserver();

        var answer = await Chain(observer, TimeSpan.FromSeconds(30), clock, links: [("a", a), ("b", b)])
            .ChatAsync(Hello, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, b.Calls);
        Assert.Empty(observer.Taken);
        Assert.Equal("FallbackExhausted", answer.ErrorType);
        Assert.Contains("budget", answer.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_later_profile_does_not_receive_the_identity_of_the_first_one()
    {
        var baseA = LlmConfig.Create("model-a") with { ApiKey = "key-a", BaseUrl = new Uri("https://a.example/"), TimeoutSeconds = 60 };
        var a = new FakeScriptedLlmProvider { BaseConfig = baseA, Response = FakeScriptedLlmProvider.Http(HttpStatusCode.ServiceUnavailable) };
        var b = new FakeScriptedLlmProvider { BaseConfig = LlmConfig.Create("model-b") with { ApiKey = "key-b" } };
        var chain = new FallbackLlmProvider(
            "main", [new FallbackLink("a", a, baseA), new FallbackLink("b", b)],
            new FakeLlmFallbackObserver(), NullLogger.Instance, null, TimeProvider.System);

        await chain.ChatAsync(Hello, baseA with { Temperature = 0.3 }, TestContext.Current.CancellationToken);

        Assert.Equal("model-a", a.LastConfig!.Model);
        Assert.Equal("key-a", a.LastConfig.ApiKey);
        Assert.NotNull(b.LastConfig);
        Assert.Equal("model-b", b.LastConfig.Model);
        Assert.Equal("key-b", b.LastConfig.ApiKey);
        Assert.Null(b.LastConfig.BaseUrl);
        Assert.Null(b.LastConfig.TimeoutSeconds);
        Assert.Equal(0.3, b.LastConfig.Temperature);
        Assert.Same(baseA, chain.BaseConfig);
    }

    [Fact]
    public async Task Profile_level_settings_of_the_first_profile_never_reach_a_later_one()
    {
        var baseA = LlmConfig.Create("model-a") with
        {
            MaxTokens = 1000,
            SystemMessage = "primary persona",
            StopSequences = ["END-A"],
            CustomParameters = new Dictionary<string, object> { ["only_a"] = 1 },
        };
        var baseB = LlmConfig.Create("model-b") with
        {
            MaxTokens = 200,
            CustomParameters = new Dictionary<string, object> { ["only_b"] = 2 },
        };
        var a = new FakeScriptedLlmProvider { BaseConfig = baseA, Response = FakeScriptedLlmProvider.Http(HttpStatusCode.ServiceUnavailable) };
        var b = new FakeScriptedLlmProvider { BaseConfig = baseB };
        var chain = new FallbackLlmProvider(
            "main", [new FallbackLink("a", a, baseA), new FallbackLink("b", b, baseB)],
            new FakeLlmFallbackObserver(), NullLogger.Instance, null, TimeProvider.System);

        await chain.ChatAsync(Hello, baseA with { Temperature = 0.3 }, TestContext.Current.CancellationToken);

        Assert.Equal(1000, a.LastConfig!.MaxTokens);
        Assert.NotNull(b.LastConfig);
        Assert.Equal(200, b.LastConfig.MaxTokens);
        Assert.Null(b.LastConfig.SystemMessage);
        Assert.Empty(b.LastConfig.StopSequences);
        Assert.True(b.LastConfig.CustomParameters.ContainsKey("only_b"));
        Assert.False(b.LastConfig.CustomParameters.ContainsKey("only_a"));
        Assert.Equal(0.3, b.LastConfig.Temperature);
    }

    [Fact]
    public async Task A_setting_the_call_sets_applies_to_every_profile()
    {
        var baseA = LlmConfig.Create("model-a") with { MaxTokens = 1000 };
        var baseB = LlmConfig.Create("model-b") with { MaxTokens = 200 };
        var a = new FakeScriptedLlmProvider { BaseConfig = baseA, Response = FakeScriptedLlmProvider.Http(HttpStatusCode.ServiceUnavailable) };
        var b = new FakeScriptedLlmProvider { BaseConfig = baseB };
        var chain = new FallbackLlmProvider(
            "main", [new FallbackLink("a", a, baseA), new FallbackLink("b", b, baseB)],
            new FakeLlmFallbackObserver(), NullLogger.Instance, null, TimeProvider.System);

        await chain.ChatAsync(Hello, baseA with { MaxTokens = 50 }, TestContext.Current.CancellationToken);

        Assert.Equal(50, a.LastConfig!.MaxTokens);
        Assert.NotNull(b.LastConfig);
        Assert.Equal(50, b.LastConfig.MaxTokens);
    }

    [Theory]
    [InlineData("TaskCanceledException")]
    [InlineData("OperationCanceledException")]
    public async Task A_cancellation_a_provider_reports_in_band_is_not_failed_over(string errorType)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var a = new FakeScriptedLlmProvider { Response = FakeScriptedLlmProvider.Failed("The request was canceled", errorType) };
        var b = new FakeScriptedLlmProvider();
        var observer = new FakeLlmFallbackObserver();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Chain(observer, links: [("a", a), ("b", b)])
            .ChatAsync(Hello, cancellationToken: cancelled.Token));

        Assert.Equal(0, b.Calls);
        Assert.Empty(observer.Taken);
    }

    [Fact]
    public void The_chain_reports_the_first_profile_as_its_provider_name_and_streams()
    {
        var a = new FakeScriptedLlmProvider { Name = "vendor-a" };
        var chain = Chain(new FakeLlmFallbackObserver(), links: [("a", a), ("b", new FakeScriptedLlmProvider { Name = "vendor-b" })]);

        Assert.Equal("vendor-a", chain.Name);
        Assert.True(chain.SupportsStreaming);
    }
}
