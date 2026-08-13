using System.Net;
using Orkeon.Studio.Core.Llm;
using Orkeon.Studio.Core.Presets;
using Orkeon.Studio.Core.Tests.Doubles;

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

        var result = await ProbeAsync(handler, OrkeonCliDefaults.OpenAI, apiKey: "sk-test");

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

        var result = await ProbeAsync(handler, OrkeonCliDefaults.Anthropic, apiKey: "sk-ant");

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

        var result = await ProbeAsync(handler, OrkeonCliDefaults.OpenAI);

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

        var result = await ProbeAsync(handler, OrkeonCliDefaults.OpenAI, apiKey: "sk-wrong");

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

        var result = await ProbeAsync(handler, OrkeonCliDefaults.OllamaDefault);

        Assert.False(result.Succeeded);
        Assert.Contains("Connection refused", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_endpoint_that_never_answers_gives_up_on_the_probes_own_deadline()
    {
        using var handler = new StubHttpMessageHandler { Delay = TimeSpan.FromMinutes(1) };

        var result = await ProbeAsync(
            handler, OrkeonCliDefaults.OpenAI, timeout: TimeSpan.FromMilliseconds(50));

        Assert.False(result.Succeeded);
        Assert.Contains("no answer within", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_deadline_is_five_seconds() =>
        Assert.Equal(TimeSpan.FromSeconds(5), new LlmProbeRequest().Timeout);

    [Fact]
    public async Task Cancelling_the_probe_propagates_the_cancellation()
    {
        using var handler = new StubHttpMessageHandler { Delay = TimeSpan.FromMinutes(1) };
        using var client = new HttpClient(handler, disposeHandler: false);
        using var probe = new HttpLlmEndpointProbe(client);
        using var cts = new CancellationTokenSource();

        var running = probe.ProbeAsync(new LlmProbeRequest { BaseUrl = OrkeonCliDefaults.OpenAI }, cts.Token);
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
}
