using Orkeon.Constants.Llm;
using System.Net;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Tests.Doubles;

namespace Orkeon.Studio.Core.Tests.Llm;

/// <summary>
/// The balance probe (STUDIO-33). The three HTTP dialects are driven with the example answers
/// of the vendors' own reference pages (read 2026-09-24); every other provider must come back
/// with an explicit verdict and without a request. No test here touches the network.
/// </summary>
public sealed class HttpProviderBalanceProbeTests
{
    private const string Key = "sk-live-0123456789";

    /// <summary>The example answer of api-docs.deepseek.com/api/get-user-balance.</summary>
    private const string DeepSeekAnswer = """
        {
          "is_available": true,
          "balance_infos": [
            { "currency": "CNY", "total_balance": "110.00", "granted_balance": "10.00", "topped_up_balance": "100.00" }
          ]
        }
        """;

    /// <summary>The example answer of platform.kimi.ai/docs/api/balance (and of its .com twin).</summary>
    private const string KimiAnswer = """
        {
          "code": 0,
          "data": { "available_balance": 49.58894, "voucher_balance": 46.58893, "cash_balance": 3.00001 },
          "scode": "0x0",
          "status": true
        }
        """;

    /// <summary>The key fields of the example answer of openrouter.ai's "Get current API key".</summary>
    private const string OpenRouterAnswer = """
        {
          "data": {
            "label": "sk-or-v1-au7...890",
            "limit": 100,
            "limit_remaining": 74.5,
            "limit_reset": "monthly",
            "usage": 25.5,
            "is_free_tier": false
          }
        }
        """;

    /// <summary>
    /// Runs one probe over <paramref name="handler"/>, which stays owned by the test so its
    /// recorded requests can be asserted afterwards.
    /// </summary>
    private static async Task<ProviderBalanceResult> ProbeAsync(
        StubHttpMessageHandler handler,
        string? baseUrl,
        string? apiKey = Key,
        TimeSpan? timeout = null,
        TimeProvider? time = null)
    {
        using var client = new HttpClient(handler, disposeHandler: false);
        using var probe = new HttpProviderBalanceProbe(client, time);

        var request = new LlmProbeRequest { BaseUrl = baseUrl, ApiKey = apiKey };
        if (timeout is { } value)
            request = request with { Timeout = value };

        return await probe.ProbeAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DeepSeek_reports_its_balance_split_into_granted_and_topped_up_parts()
    {
        using var handler = new StubHttpMessageHandler { Body = DeepSeekAnswer };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.DeepSeek);

        Assert.Equal(ProviderBalanceStatus.Available, result.Status);
        Assert.Equal(LlmProviderKeys.DeepSeek, result.Provider);
        Assert.Equal(ProviderBalanceScope.Account, result.Scope);
        var amount = Assert.Single(result.Amounts);
        Assert.Equal(new ProviderBalanceAmount("CNY", 110.00m, Granted: 10.00m, Paid: 100.00m), amount);
        Assert.Equal("GET", handler.LastRequest.Method);
        Assert.Equal(new Uri("https://api.deepseek.com/user/balance"), handler.LastRequest.Uri);
        Assert.Equal($"Bearer {Key}", handler.LastRequest.Authorization);
    }

    [Fact]
    public async Task DeepSeek_behind_a_v1_base_url_is_still_asked_on_the_host_root()
    {
        // The vendor accepts both base URLs for chat; its balance hangs off the root only.
        using var handler = new StubHttpMessageHandler { Body = DeepSeekAnswer };

        await ProbeAsync(handler, "https://api.deepseek.com/v1");

        Assert.Equal(new Uri("https://api.deepseek.com/user/balance"), handler.LastRequest.Uri);
    }

    [Fact]
    public async Task DeepSeek_lists_one_balance_per_currency()
    {
        using var handler = new StubHttpMessageHandler
        {
            Body = """
                {
                  "is_available": true,
                  "balance_infos": [
                    { "currency": "CNY", "total_balance": "110.00", "granted_balance": "10.00", "topped_up_balance": "100.00" },
                    { "currency": "USD", "total_balance": "5.50", "granted_balance": "0.00", "topped_up_balance": "5.50" }
                  ]
                }
                """,
        };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.DeepSeek);

        Assert.Equal(["CNY", "USD"], result.Amounts.Select(a => a.Currency));
        Assert.Equal(5.50m, result.Amounts[1].Available);
    }

    [Fact]
    public async Task Kimi_international_reports_its_balance_in_us_dollars()
    {
        using var handler = new StubHttpMessageHandler { Body = KimiAnswer };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.Kimi);

        Assert.Equal(ProviderBalanceStatus.Available, result.Status);
        Assert.Equal(new ProviderBalanceAmount("USD", 49.58894m, Granted: 46.58893m, Paid: 3.00001m), Assert.Single(result.Amounts));
        Assert.Equal(new Uri("https://api.moonshot.ai/v1/users/me/balance"), handler.LastRequest.Uri);
        Assert.Equal($"Bearer {Key}", handler.LastRequest.Authorization);
    }

    [Fact]
    public async Task Kimi_china_is_asked_on_the_china_host_and_answers_in_yuan()
    {
        // D-04: the request follows the profile's host. A .cn key is refused by the .ai host,
        // and the .cn platform documents its amounts in yuan.
        using var handler = new StubHttpMessageHandler { Body = KimiAnswer };

        var result = await ProbeAsync(handler, $"https://{LlmProviderEndpoints.KimiChinaHost}/v1");

        Assert.Equal(new Uri("https://api.moonshot.cn/v1/users/me/balance"), handler.LastRequest.Uri);
        Assert.Equal("CNY", Assert.Single(result.Amounts).Currency);
    }

    [Fact]
    public async Task A_kimi_account_in_arrears_reports_a_negative_cash_part()
    {
        using var handler = new StubHttpMessageHandler
        {
            Body = """{ "code": 0, "data": { "available_balance": 10, "voucher_balance": 10, "cash_balance": -3.5 }, "status": true }""",
        };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.Kimi);

        Assert.Equal(new ProviderBalanceAmount("USD", 10m, Granted: 10m, Paid: -3.5m), Assert.Single(result.Amounts));
    }

    [Fact]
    public async Task OpenRouter_reports_what_the_key_may_still_spend_under_its_limit()
    {
        using var handler = new StubHttpMessageHandler { Body = OpenRouterAnswer };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.OpenRouter);

        Assert.Equal(ProviderBalanceStatus.Available, result.Status);
        Assert.Equal(ProviderBalanceScope.ApiKey, result.Scope);
        Assert.Equal(new ProviderBalanceAmount("USD", 74.5m), Assert.Single(result.Amounts));
        Assert.Equal(new Uri("https://openrouter.ai/api/v1/key"), handler.LastRequest.Uri);
        Assert.Equal($"Bearer {Key}", handler.LastRequest.Authorization);
    }

    [Fact]
    public async Task OpenRouter_without_a_key_limit_leaves_the_account_credits_to_a_management_key()
    {
        using var handler = new StubHttpMessageHandler
        {
            Body = """{ "data": { "limit": null, "limit_remaining": null, "usage": 25.5, "is_free_tier": false } }""",
        };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.OpenRouter);

        Assert.Equal(ProviderBalanceStatus.AdminKeyRequired, result.Status);
        Assert.Empty(result.Amounts);
        Assert.Contains("management key", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_available_balance_is_stamped_with_the_time_it_was_read()
    {
        using var handler = new StubHttpMessageHandler { Body = DeepSeekAnswer };
        var time = new StubTimeProvider();

        var result = await ProbeAsync(handler, LlmProviderEndpoints.DeepSeek, time: time);

        Assert.Equal(time.Now, result.CheckedAt);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refused_key_is_reported_as_an_authentication_refusal(HttpStatusCode status)
    {
        using var handler = new StubHttpMessageHandler { StatusCode = status };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.DeepSeek);

        Assert.Equal(ProviderBalanceStatus.AuthenticationRefused, result.Status);
        Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_without_the_balance_endpoint_is_reported_as_not_exposed()
    {
        using var handler = new StubHttpMessageHandler { StatusCode = HttpStatusCode.NotFound };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.Kimi);

        Assert.Equal(ProviderBalanceStatus.NotExposed, result.Status);
        Assert.Contains("404", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Any_other_error_status_is_reported_as_a_network_error()
    {
        using var handler = new StubHttpMessageHandler { StatusCode = HttpStatusCode.ServiceUnavailable };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.OpenRouter);

        Assert.Equal(ProviderBalanceStatus.NetworkError, result.Status);
        Assert.Contains("503", result.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LlmProviderEndpoints.DeepSeek, "not json at all")]
    [InlineData(LlmProviderEndpoints.DeepSeek, "{}")]
    [InlineData(LlmProviderEndpoints.DeepSeek, """{ "balance_infos": [] }""")]
    [InlineData(LlmProviderEndpoints.DeepSeek, """{ "balance_infos": [ { "currency": "CNY", "total_balance": "a lot" } ] }""")]
    [InlineData(LlmProviderEndpoints.Kimi, """{ "code": 0, "data": { "voucher_balance": 1 } }""")]
    [InlineData(LlmProviderEndpoints.Kimi, "[]")]
    [InlineData(LlmProviderEndpoints.OpenRouter, """{ "data": { "limit": 100 } }""")]
    [InlineData(LlmProviderEndpoints.OpenRouter, """{ "data": { "limit_remaining": "plenty" } }""")]
    public async Task An_answer_outside_the_documented_shape_is_reported_as_unexpected(string baseUrl, string body)
    {
        using var handler = new StubHttpMessageHandler { Body = body };

        var result = await ProbeAsync(handler, baseUrl);

        Assert.Equal(ProviderBalanceStatus.UnexpectedAnswer, result.Status);
        Assert.Empty(result.Amounts);
    }

    [Fact]
    public async Task A_provider_that_never_answers_gives_up_on_the_probes_own_deadline()
    {
        using var handler = new StubHttpMessageHandler { Delay = TimeSpan.FromMinutes(1) };

        var result = await ProbeAsync(
            handler, LlmProviderEndpoints.DeepSeek, timeout: TimeSpan.FromMilliseconds(50));

        Assert.Equal(ProviderBalanceStatus.NetworkError, result.Status);
        Assert.Contains("No answer within", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_transport_failure_comes_back_as_a_network_error_not_an_exception()
    {
        using var handler = new StubHttpMessageHandler
        {
            FailWith = new HttpRequestException("Name or service not known (api.moonshot.ai:443)"),
        };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.Kimi);

        Assert.Equal(ProviderBalanceStatus.NetworkError, result.Status);
        Assert.Contains("Name or service not known", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelling_the_probe_propagates_the_cancellation()
    {
        using var handler = new StubHttpMessageHandler { Delay = TimeSpan.FromMinutes(1) };
        using var client = new HttpClient(handler, disposeHandler: false);
        using var probe = new HttpProviderBalanceProbe(client);
        using var cts = new CancellationTokenSource();

        var running = probe.ProbeAsync(
            new LlmProbeRequest { BaseUrl = LlmProviderEndpoints.DeepSeek, ApiKey = Key }, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.OK)]
    public async Task No_detail_ever_repeats_the_key_or_the_body_of_the_answer(HttpStatusCode status)
    {
        // D-05, a deliberate difference with the connectivity probe: an answer may carry
        // account data, so no part of it is quoted back — and neither is the key it echoes.
        using var handler = new StubHttpMessageHandler
        {
            StatusCode = status,
            Body = $$"""{ "error": { "message": "account acct-4242 of {{Key}} owes 12.34" } }""",
        };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.DeepSeek);

        Assert.DoesNotContain(Key, result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("acct-4242", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failure_that_names_the_key_has_it_masked()
    {
        using var handler = new StubHttpMessageHandler
        {
            FailWith = new HttpRequestException($"the proxy rejected Bearer {Key}"),
        };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.OpenRouter);

        Assert.Equal(ProviderBalanceStatus.NetworkError, result.Status);
        Assert.DoesNotContain(Key, result.Detail, StringComparison.Ordinal);
        Assert.Contains("the proxy rejected", result.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_provider_with_a_balance_but_no_key_is_refused_without_a_request(string? apiKey)
    {
        using var handler = new StubHttpMessageHandler { Body = DeepSeekAnswer };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.DeepSeek, apiKey: apiKey);

        Assert.Equal(ProviderBalanceStatus.AuthenticationRefused, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Ollama_has_no_account_so_its_balance_is_not_applicable()
    {
        using var handler = new StubHttpMessageHandler();

        var result = await ProbeAsync(handler, LlmProviderEndpoints.OllamaDefault, apiKey: null);

        Assert.Equal(ProviderBalanceStatus.NotApplicable, result.Status);
        Assert.Equal(LlmProviderKeys.Ollama, result.Provider);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    // No API returns the balance to any key (spend-only admin APIs included).
    [InlineData(LlmProviderEndpoints.OpenAI, ProviderBalanceStatus.NotExposed)]
    [InlineData("https://my-resource.openai.azure.com/", ProviderBalanceStatus.NotExposed)]
    [InlineData(LlmProviderEndpoints.Anthropic, ProviderBalanceStatus.NotExposed)]
    [InlineData(LlmProviderEndpoints.Mistral, ProviderBalanceStatus.NotExposed)]
    [InlineData(LlmProviderEndpoints.Gemini, ProviderBalanceStatus.NotExposed)]
    [InlineData(LlmProviderEndpoints.Together, ProviderBalanceStatus.NotExposed)]
    [InlineData(LlmProviderEndpoints.HuggingFace, ProviderBalanceStatus.NotExposed)]
    [InlineData(LlmProviderEndpoints.Zai, ProviderBalanceStatus.NotExposed)]
    [InlineData(LlmProviderEndpoints.MiniMax, ProviderBalanceStatus.NotExposed)]
    [InlineData("https://api.minimaxi.com/v1", ProviderBalanceStatus.NotExposed)]
    [InlineData(LlmProviderEndpoints.Mammouth, ProviderBalanceStatus.NotExposed)]
    [InlineData("https://llm.example.com/v1", ProviderBalanceStatus.NotExposed)]
    // The balance exists behind an API, for an administrative credential only.
    [InlineData(LlmProviderEndpoints.Grok, ProviderBalanceStatus.AdminKeyRequired)]
    [InlineData(LlmProviderEndpoints.Qwen, ProviderBalanceStatus.AdminKeyRequired)]
    // No account to ask.
    [InlineData(LlmProviderEndpoints.OllamaDefault, ProviderBalanceStatus.NotApplicable)]
    [InlineData(LlmProviderEndpoints.DockerModelRunner, ProviderBalanceStatus.NotApplicable)]
    [InlineData("http://localhost:1234/v1", ProviderBalanceStatus.NotApplicable)]
    [InlineData(null, ProviderBalanceStatus.NotApplicable)]
    public async Task Every_provider_without_a_readable_balance_gets_an_explicit_verdict_without_a_request(
        string? baseUrl,
        ProviderBalanceStatus expected)
    {
        using var handler = new StubHttpMessageHandler();

        var result = await ProbeAsync(handler, baseUrl);

        Assert.Equal(expected, result.Status);
        Assert.Empty(handler.Requests);
        Assert.Empty(result.Amounts);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
    }

    [Fact]
    public async Task A_verdict_without_a_balance_points_at_the_vendor_console()
    {
        using var handler = new StubHttpMessageHandler();

        var result = await ProbeAsync(handler, LlmProviderEndpoints.OpenAI);

        Assert.Equal(new Uri("https://platform.openai.com/api-keys"), result.ConsoleUrl);
    }

    [Fact]
    public async Task The_kimi_china_host_is_not_sent_to_the_international_console()
    {
        // The card's console serves the .ai accounts; a .cn key belongs to another platform.
        using var handler = new StubHttpMessageHandler { StatusCode = HttpStatusCode.Unauthorized };

        var result = await ProbeAsync(handler, $"https://{LlmProviderEndpoints.KimiChinaHost}/v1");

        Assert.Null(result.ConsoleUrl);
    }
}
