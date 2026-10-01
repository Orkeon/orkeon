using Orkeon.Constants.Llm;
using System.Net;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Tests.Doubles;
using System.Text.Json;

namespace Orkeon.Studio.Core.Tests.Llm;

/// <summary>
/// The connectivity probe behind the "Test connection" button (SPEC §4.2). Every case runs
/// against <see cref="StubHttpMessageHandler"/> — no test here touches the network.
/// </summary>
public sealed class HttpLlmEndpointProbeTests
{
    /// <summary>
    /// Runs one probe over <paramref name="handler"/>. The handler stays owned by the test
    /// (<c>disposeHandler: false</c>) so its recorded requests can be asserted afterwards.
    /// </summary>
    private static async Task<LlmProbeResult> ProbeAsync(
        StubHttpMessageHandler handler,
        string? baseUrl,
        string? apiKey = null,
        TimeSpan? timeout = null)
    {
        using var client = new HttpClient(handler, disposeHandler: false);
        using var probe = new HttpLlmEndpointProbe(client);

        var request = new LlmProbeRequest { BaseUrl = baseUrl, ApiKey = apiKey };
        if (timeout is { } value)
            request = request with { Timeout = value };

        return await probe.ProbeAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task An_openai_compatible_endpoint_is_asked_for_its_models_with_a_bearer_token()
    {
        using var handler = new StubHttpMessageHandler
        {
            Body = """{ "data": [ { "id": "gpt-4o" }, { "id": "gpt-4o-mini" } ] }""",
        };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.OpenAI, apiKey: "sk-test");

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.ModelCount);
        Assert.Contains("2 model(s)", result.Message, StringComparison.Ordinal);
        Assert.Equal("GET", handler.LastRequest.Method);
        Assert.Equal(new Uri("https://api.openai.com/v1/models"), handler.LastRequest.Uri);
        Assert.Equal("Bearer sk-test", handler.LastRequest.Authorization);
    }

    [Fact]
    public async Task An_endpoint_without_a_key_is_probed_unauthenticated()
    {
        using var handler = new StubHttpMessageHandler { Body = """{ "data": [] }""" };

        var result = await ProbeAsync(handler, "https://api.deepseek.com");

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ModelCount);
        Assert.Null(handler.LastRequest.Authorization);
    }

    [Theory]
    // Ollama's catalogue hangs off the server root, so a base URL carrying a path
    // (the OpenAI-compatible form some users configure) must have it dropped.
    [InlineData("http://localhost:11434")]
    [InlineData("http://localhost:11434/v1")]
    public async Task Ollama_is_asked_for_its_tags_on_the_server_root(string baseUrl)
    {
        using var handler = new StubHttpMessageHandler
        {
            Body = """{ "models": [ { "name": "llama3.2" }, { "name": "qwen2.5" }, { "name": "phi4" } ] }""",
        };

        var result = await ProbeAsync(handler, baseUrl);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.ModelCount);
        Assert.Equal(new Uri("http://localhost:11434/api/tags"), handler.LastRequest.Uri);
    }

    [Fact]
    public async Task Anthropic_is_probed_with_its_own_headers()
    {
        using var handler = new StubHttpMessageHandler { Body = """{ "data": [ { "id": "claude" } ] }""" };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.Anthropic, apiKey: "sk-ant");

        Assert.True(result.Succeeded);
        Assert.Equal(new Uri("https://api.anthropic.com/v1/models"), handler.LastRequest.Uri);
        Assert.Equal("sk-ant", handler.LastRequest.ApiKeyHeader);
        Assert.Null(handler.LastRequest.Authorization);
    }

    [Fact]
    public async Task A_reachable_endpoint_answering_an_unknown_shape_still_succeeds()
    {
        // The question the button asks is "does it answer", not "what does it serve".
        using var handler = new StubHttpMessageHandler { Body = "not json at all" };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.OpenAI);

        Assert.True(result.Succeeded);
        Assert.Null(result.ModelCount);
        Assert.Equal("Endpoint reachable.", result.Message);
    }

    [Fact]
    public async Task A_rejected_key_is_reported_with_the_status_and_the_body()
    {
        using var handler = new StubHttpMessageHandler
        {
            StatusCode = HttpStatusCode.Unauthorized,
            Body = """{ "error": { "message": "Incorrect API key provided" } }""",
        };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.OpenAI, apiKey: "sk-wrong");

        Assert.False(result.Succeeded);
        Assert.Contains("401", result.Message, StringComparison.Ordinal);
        Assert.Contains("Incorrect API key provided", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_transport_failure_comes_back_as_a_result_not_an_exception()
    {
        using var handler = new StubHttpMessageHandler
        {
            FailWith = new HttpRequestException("Connection refused (localhost:11434)"),
        };

        var result = await ProbeAsync(handler, LlmProviderEndpoints.OllamaDefault);

        Assert.False(result.Succeeded);
        Assert.Contains("Connection refused", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_endpoint_that_never_answers_gives_up_on_the_probes_own_deadline()
    {
        using var handler = new StubHttpMessageHandler { Delay = TimeSpan.FromMinutes(1) };

        var result = await ProbeAsync(
            handler, LlmProviderEndpoints.OpenAI, timeout: TimeSpan.FromMilliseconds(50));

        Assert.False(result.Succeeded);
        Assert.Contains("no answer within", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_deadline_is_thirty_seconds() =>
        Assert.Equal(TimeSpan.FromSeconds(30), new LlmProbeRequest().Timeout);

    [Theory]
    // STUDIO-43: 30 s by default, bounded by the profile's own timeout when that is shorter —
    // never the profile's 600 s, too long to keep a button waiting.
    [InlineData(null, 30)]
    [InlineData(600, 30)]
    [InlineData(10, 10)]
    [InlineData(0, 30)]
    public void The_deadline_is_bounded_by_a_shorter_profile_timeout(int? profileSeconds, int expected) =>
        Assert.Equal(TimeSpan.FromSeconds(expected), LlmProbeRequest.TimeoutFor(profileSeconds));

    [Fact]
    public async Task Cancelling_the_probe_propagates_the_cancellation()
    {
        using var handler = new StubHttpMessageHandler { Delay = TimeSpan.FromMinutes(1) };
        using var client = new HttpClient(handler, disposeHandler: false);
        using var probe = new HttpLlmEndpointProbe(client);
        using var cts = new CancellationTokenSource();

        var running = probe.ProbeAsync(new LlmProbeRequest { BaseUrl = LlmProviderEndpoints.OpenAI }, cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_endpoint_that_is_not_configured_is_reported_without_a_request(string? baseUrl)
    {
        using var handler = new StubHttpMessageHandler();

        var result = await ProbeAsync(handler, baseUrl);

        Assert.False(result.Succeeded);
        Assert.Contains("nothing to reach", result.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_url_with_no_scheme_at_all_is_reported_without_a_request()
    {
        using var handler = new StubHttpMessageHandler();

        var result = await ProbeAsync(handler, "api.openai.com/v1");

        Assert.False(result.Succeeded);
        Assert.Contains("not an absolute URL", result.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_host_typed_without_its_scheme_is_reported_without_a_request()
    {
        // "localhost:11434" parses as an absolute URI whose scheme is "localhost" — the
        // most common way to half-type a local endpoint, and not something to send.
        using var handler = new StubHttpMessageHandler();

        var result = await ProbeAsync(handler, "localhost:11434");

        Assert.False(result.Succeeded);
        Assert.Contains("not an http:// or https:// URL", result.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Azure_openai_is_declined_because_it_has_no_catalogue_to_probe()
    {
        using var handler = new StubHttpMessageHandler();

        var result = await ProbeAsync(handler, "https://my-deployment.openai.azure.com/");

        Assert.False(result.Succeeded);
        Assert.Contains("deployments", result.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }

    // ── STUDIO-43: the two-stage probe and its diagnostic ──

    private const string ZaiKey = "zai-secret-key-1234";

    private static LlmProbeRequest ZaiProfile(bool? thinking = false) => new()
    {
        BaseUrl = LlmProviderEndpoints.Zai,
        ApiKey = ZaiKey,
        Model = "glm-5.2",
        ThinkingEnabled = thinking,
        CheckCompletion = true,
    };

    private static async Task<LlmProbeResult> ProbeAsync(
        StubHttpMessageHandler handler, LlmProbeRequest request, TimeProvider? time = null)
    {
        using var client = new HttpClient(handler, disposeHandler: false);
        using var probe = new HttpLlmEndpointProbe(client, time);
        return await probe.ProbeAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_slow_catalogue_still_passes_within_the_thirty_second_deadline()
    {
        // The Z.AI report: a first /models that pays DNS and TLS took more than 5 s.
        var time = new ManualTimeProvider();
        using var handler = new StubHttpMessageHandler
        {
            Body = """{ "data": [ { "id": "glm-5.2" } ] }""",
            OnSend = (_, _) =>
            {
                time.Advance(TimeSpan.FromSeconds(10));
                return Task.CompletedTask;
            },
        };

        var result = await ProbeAsync(handler, new LlmProbeRequest { BaseUrl = LlmProviderEndpoints.Zai, ApiKey = ZaiKey }, time);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Elapsed);
    }

    [Fact]
    public async Task A_timeout_names_the_step_the_url_and_the_time_waited()
    {
        var time = new ManualTimeProvider();
        using var handler = new StubHttpMessageHandler
        {
            OnSend = async (_, token) =>
            {
                time.Advance(TimeSpan.FromSeconds(31));
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
        };

        var result = await ProbeAsync(handler, new LlmProbeRequest { BaseUrl = LlmProviderEndpoints.Zai, ApiKey = ZaiKey }, time);

        Assert.False(result.Succeeded);
        Assert.Equal(LlmProbeFailure.Timeout, result.Failure);
        Assert.Equal(LlmProbeStage.Models, result.Stage);
        Assert.Equal("https://api.z.ai/api/paas/v4/models", result.Url);
        Assert.Equal(TimeSpan.FromSeconds(31), result.Elapsed);
        Assert.Contains("https://api.z.ai/api/paas/v4/models", result.Message, StringComparison.Ordinal);
        Assert.Contains("31", result.Message, StringComparison.Ordinal);
        Assert.Contains("no answer within 30 s", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_transport_failure_reports_the_innermost_exception()
    {
        using var handler = new StubHttpMessageHandler
        {
            FailWith = new HttpRequestException(
                "An error occurred while sending the request.",
                new IOException("Unable to read data from the transport connection.",
                    new System.Net.Sockets.SocketException(10054))),
        };

        var result = await ProbeAsync(handler, ZaiProfile());

        Assert.False(result.Succeeded);
        Assert.Equal(LlmProbeFailure.Transport, result.Failure);
        Assert.Contains("HttpRequestException", result.Detail, StringComparison.Ordinal);
        Assert.Contains("An error occurred while sending the request.", result.Detail, StringComparison.Ordinal);
        Assert.Contains("SocketException", result.Detail, StringComparison.Ordinal);
        Assert.Contains("SocketException", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_completion_carries_the_profiles_model_and_disabled_thinking()
    {
        using var handler = new StubHttpMessageHandler { Body = """{ "data": [ { "id": "glm-5.2" } ] }""" };

        var result = await ProbeAsync(handler, ZaiProfile(thinking: false));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, handler.Requests.Count);
        var completion = handler.Requests[1];
        Assert.Equal("POST", completion.Method);
        Assert.Equal(new Uri("https://api.z.ai/api/paas/v4/chat/completions"), completion.Uri);
        Assert.Equal($"Bearer {ZaiKey}", completion.Authorization);

        using var body = JsonDocument.Parse(completion.Body!);
        Assert.Equal("glm-5.2", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(16, body.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("disabled", body.RootElement.GetProperty("thinking").GetProperty("type").GetString());
        Assert.Equal(LlmProbeStage.Completion, result.Stage);
        Assert.Equal("glm-5.2", result.Model);
    }

    [Fact]
    public async Task The_providers_default_thinking_sends_no_thinking_field()
    {
        using var handler = new StubHttpMessageHandler { Body = "{}" };

        await ProbeAsync(handler, ZaiProfile(thinking: null));

        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.False(body.RootElement.TryGetProperty("thinking", out _));
    }

    [Theory]
    // The switch is written the way the runtime writes it for each dialect.
    [InlineData(LlmProviderEndpoints.Qwen, "enable_thinking")]
    [InlineData(LlmProviderEndpoints.OpenRouter, "reasoning")]
    [InlineData(LlmProviderEndpoints.DeepSeek, "thinking")]
    public async Task Each_dialect_carries_the_thinking_switch_in_its_own_field(string endpoint, string field)
    {
        using var handler = new StubHttpMessageHandler { Body = "{}" };

        await ProbeAsync(handler, ZaiProfile(thinking: false) with { BaseUrl = endpoint });

        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.True(body.RootElement.TryGetProperty(field, out _), handler.Requests[1].Body);
    }

    [Fact]
    public async Task An_effort_only_provider_gets_no_switch_it_would_ignore()
    {
        using var handler = new StubHttpMessageHandler { Body = "{}" };

        await ProbeAsync(handler, ZaiProfile(thinking: false) with { BaseUrl = LlmProviderEndpoints.OpenAI });

        using var body = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.False(body.RootElement.TryGetProperty("thinking", out _));
        // OpenAI retired max_tokens on its current chat models, like the runtime knows.
        Assert.Equal(16, body.RootElement.GetProperty("max_completion_tokens").GetInt32());
    }

    [Fact]
    public async Task Ollama_and_anthropic_get_the_completion_in_their_own_dialect()
    {
        using var ollama = new StubHttpMessageHandler { Body = "{}" };
        await ProbeAsync(ollama, ZaiProfile(thinking: false) with { BaseUrl = "http://localhost:11434/v1" });
        Assert.Equal(new Uri("http://localhost:11434/api/chat"), ollama.Requests[1].Uri);
        using (var body = JsonDocument.Parse(ollama.Requests[1].Body!))
        {
            Assert.False(body.RootElement.GetProperty("think").GetBoolean());
            Assert.False(body.RootElement.GetProperty("stream").GetBoolean());
        }

        using var anthropic = new StubHttpMessageHandler { Body = "{}" };
        await ProbeAsync(anthropic, ZaiProfile(thinking: false) with { BaseUrl = LlmProviderEndpoints.Anthropic });
        Assert.Equal(new Uri("https://api.anthropic.com/v1/messages"), anthropic.Requests[1].Uri);
        Assert.Equal(ZaiKey, anthropic.Requests[1].ApiKeyHeader);
        using (var body = JsonDocument.Parse(anthropic.Requests[1].Body!))
            Assert.Equal("disabled", body.RootElement.GetProperty("thinking").GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_refused_completion_is_reported_as_the_completion_step_after_a_good_catalogue()
    {
        using var handler = new StubHttpMessageHandler
        {
            Answer = request => request.Method == "GET"
                ? new StubHttpAnswer(HttpStatusCode.OK, """{ "data": [ { "id": "glm-5.3" } ] }""")
                : new StubHttpAnswer(HttpStatusCode.BadRequest,
                    """{ "error": { "message": "thinking cannot be disabled for this model" } }"""),
        };

        var result = await ProbeAsync(handler, ZaiProfile(thinking: false) with { Model = "glm-5.3" });

        Assert.False(result.Succeeded);
        Assert.Equal(LlmProbeStage.Completion, result.Stage);
        Assert.Equal(LlmProbeFailure.HttpStatus, result.Failure);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("https://api.z.ai/api/paas/v4/chat/completions", result.Url);
        Assert.Contains("thinking cannot be disabled", result.Message, StringComparison.Ordinal);
        Assert.Contains("test request", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_model_only_the_catalogue_is_asked()
    {
        using var handler = new StubHttpMessageHandler { Body = "{}" };

        var result = await ProbeAsync(handler, ZaiProfile() with { Model = " " });

        Assert.True(result.Succeeded);
        Assert.Single(handler.Requests);
        Assert.Equal(LlmProbeStage.Models, result.Stage);
    }

    [Fact]
    public async Task The_key_never_appears_in_the_result_even_when_the_endpoint_echoes_it()
    {
        using var handler = new StubHttpMessageHandler
        {
            StatusCode = HttpStatusCode.Unauthorized,
            Body = $$"""{ "error": "Incorrect API key provided: {{ZaiKey}}" }""",
        };

        var result = await ProbeAsync(
            handler, ZaiProfile() with { BaseUrl = $"https://user:{ZaiKey}@api.z.ai/api/paas/v4?key={ZaiKey}" });

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(ZaiKey, result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ZaiKey, result.Detail ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain(ZaiKey, result.Url ?? "", StringComparison.Ordinal);
    }
}
