using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel.ValueObjects.Content;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Capabilities;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Orkeon.Tests.Shared.Doubles;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Capabilities;

/// <summary>FWK-21: the reasoning effort and the audio parts are judged against the model, not only the provider.</summary>
public class ReasoningEffortModelValidationTests
{
    private static readonly string OkBody = JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content = "ok" } } },
        usage = new { total_tokens = 1, prompt_tokens = 1, completion_tokens = 0 },
    });

    private sealed record Sent(JsonElement Body, IReadOnlyList<string> Warnings);

    private static LlmConfig Config(string model, string? effort = null) =>
        LlmConfig.Create(model, TestApiKey) with
        {
            BaseUrl = new Uri("https://provider.test/v1"),
            Thinking = effort is null ? null : new LlmThinkingConfig { Effort = effort },
        };

    /// <summary>Sends one call through the factory, the entry point a host builds its providers with.</summary>
    private static async Task<Sent> SendAsync(LlmConfig config, IModelCapabilityRegistry? registry, LlmMessage[]? messages = null)
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkBody);
        var http = new TestHttpClientFactory();
        http.RegisterClient(nameof(OpenAILlmProvider), new HttpClient(handler));
        using var loggers = new RecordingLoggerFactory();
        var factory = new LlmProviderFactory(
            http, loggers, LlmProviderFactory.MountlessFileSystem(), new LlmProviderFactoryOptions { Capabilities = registry });

        var provider = ((LlmProviderAdapter)factory.Create("openai", config)).UnderlyingProvider;
        await provider.ChatAsync(messages ?? [new LlmMessage { Role = "user", Content = "hi" }], config, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.CapturedRequests);
        var raw = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(raw);
        var warnings = loggers.Logger.Entries.Where(e => e.Level >= LogLevel.Warning).Select(e => e.Message).ToList();
        return new Sent(doc.RootElement.Clone(), warnings);
    }

    [Fact]
    public async Task An_effort_the_model_excludes_is_omitted_and_warned_about()
    {
        var sent = await SendAsync(Config("gpt-4o", "high"), registry: null);

        Assert.False(sent.Body.TryGetProperty("reasoning_effort", out _));
        var warning = Assert.Single(sent.Warnings);
        Assert.Contains("gpt-4o", warning, StringComparison.Ordinal);
        Assert.Contains("thinking.effort", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_effort_the_model_takes_is_sent_without_a_warning()
    {
        var sent = await SendAsync(Config("o3-mini", "high"), registry: null);

        Assert.Equal("high", sent.Body.GetProperty("reasoning_effort").GetString());
        Assert.Empty(sent.Warnings);
    }

    [Fact]
    public async Task An_effort_outside_the_list_of_a_model_that_has_one_is_moved_into_it_and_reported()
    {
        // Nothing lies below `low` in gpt-5-pro's [high]: the lowest level it takes is sent (#356).
        var sent = await SendAsync(Config("gpt-5-pro", "low"), registry: null);

        Assert.Equal("high", sent.Body.GetProperty("reasoning_effort").GetString());
        Assert.Contains("'high' was sent in its place", Assert.Single(sent.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_effort_is_lowered_within_the_models_list_and_reported()
    {
        // gpt-6-astra takes `low` to `xhigh` (campaign M18): `max` becomes `xhigh`.
        var sent = await SendAsync(Config("gpt-6-astra", "max"), registry: null);

        Assert.Equal("xhigh", sent.Body.GetProperty("reasoning_effort").GetString());
        Assert.Single(sent.Warnings);
    }

    [Fact]
    public async Task An_effort_is_omitted_for_a_model_with_no_reasoning_pass()
    {
        var sent = await SendAsync(Config("gpt-4o", "high"), registry: null);

        Assert.False(sent.Body.TryGetProperty("reasoning_effort", out _));
        Assert.Contains("takes no reasoning effort", Assert.Single(sent.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_option_is_omitted_for_a_model_the_host_declares_without_thinking()
    {
        var registry = new ModelCapabilityRegistry(new Dictionary<string, ModelCapabilityOverride>
        {
            ["acme-plain"] = new() { Thinking = ThinkingSupport.None },
        });

        var sent = await SendAsync(Config("acme-plain-2", "high"), registry);

        Assert.False(sent.Body.TryGetProperty("reasoning_effort", out _));
        Assert.Contains("has no reasoning pass", Assert.Single(sent.Warnings), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gpt-5.6-sol")]
    [InlineData("gpt-6-sol")]
    public async Task The_none_effort_reaches_the_wire_unchanged(string model)
    {
        // Function tools on chat/completions need `reasoning_effort: none` on these models: sent as "low",
        // or dropped because the `gpt-5` row caught `gpt-5.6`, every tool call is a 400 (campaign of 2026-11-11).
        var sent = await SendAsync(Config(model, "none"), registry: null);

        Assert.Equal("none", sent.Body.GetProperty("reasoning_effort").GetString());
    }

    [Fact]
    public async Task An_unknown_model_gets_the_effort_as_asked_and_a_warning()
    {
        var sent = await SendAsync(Config("acme-reasoner-9", "high"), registry: null);

        Assert.Equal("high", sent.Body.GetProperty("reasoning_effort").GetString());
        var warning = Assert.Single(sent.Warnings);
        Assert.Contains("acme-reasoner-9", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_hosts_override_reaches_the_provider_the_factory_builds()
    {
        var registry = new ModelCapabilityRegistry(new Dictionary<string, ModelCapabilityOverride>
        {
            ["acme-reasoner"] = new() { ReasoningEfforts = ["low"] },
        });

        var excluded = await SendAsync(Config("acme-reasoner-9", "high"), registry);
        var taken = await SendAsync(Config("acme-reasoner-9", "low"), registry);

        Assert.Equal("low", excluded.Body.GetProperty("reasoning_effort").GetString());
        Assert.Single(excluded.Warnings);
        Assert.Equal("low", taken.Body.GetProperty("reasoning_effort").GetString());
        Assert.Empty(taken.Warnings);
    }

    [Fact]
    public async Task The_hosts_override_lets_the_provider_send_audio()
    {
        var registry = new ModelCapabilityRegistry(new Dictionary<string, ModelCapabilityOverride>
        {
            ["acme-voice"] = new() { AudioInput = true },
        });
        var message = new LlmMessage
        {
            Role = "user",
            Content = "listen",
            MultiModalContent = MultiModalContent.Empty().AddText("listen").AddAudio(AudioContentPart.FromBytes([1, 2], "audio/wav")),
        };

        var sent = await SendAsync(Config("acme-voice-1"), registry, [message]);

        var content = sent.Body.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("input_audio", content[1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Audio_to_a_model_without_it_is_refused_before_the_wire_and_names_the_model()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkBody);
        var http = new TestHttpClientFactory();
        http.RegisterClient(nameof(OpenAILlmProvider), new HttpClient(handler));
        var factory = new LlmProviderFactory(http, NullLoggerFactory.Instance, LlmProviderFactory.MountlessFileSystem());
        var config = Config("gpt-4o");
        var message = new LlmMessage
        {
            Role = "user",
            Content = "listen",
            MultiModalContent = MultiModalContent.Empty().AddText("listen").AddAudio(AudioContentPart.FromBytes([1, 2], "audio/wav")),
        };

        var provider = ((LlmProviderAdapter)factory.Create("openai", config)).UnderlyingProvider;
        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.ChatAsync([message], config, TestContext.Current.CancellationToken));

        Assert.Empty(handler.CapturedRequests);
        Assert.Contains("'gpt-4o'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_unchecked_effort_warning_is_given_once_per_provider_and_model()
    {
        using var handler = TestHttpMessageHandler.CreateWithResponse(HttpStatusCode.OK, OkBody);
        var http = new TestHttpClientFactory();
        http.RegisterClient(nameof(OpenAILlmProvider), new HttpClient(handler));
        using var loggers = new RecordingLoggerFactory();
        var factory = new LlmProviderFactory(http, loggers, LlmProviderFactory.MountlessFileSystem());
        var provider = ((LlmProviderAdapter)factory.Create("openai", Config("acme-reasoner-9", "high"))).UnderlyingProvider;
        LlmMessage[] hi = [new LlmMessage { Role = "user", Content = "hi" }];

        await provider.ChatAsync(hi, Config("acme-reasoner-9", "high"), TestContext.Current.CancellationToken);
        await provider.ChatAsync(hi, Config("acme-reasoner-9", "high"), TestContext.Current.CancellationToken);
        await provider.ChatAsync(hi, Config("acme-other-1", "high"), TestContext.Current.CancellationToken);

        var warnings = loggers.Logger.Entries.Where(e => e.Level >= LogLevel.Warning).Select(e => e.Message).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.Contains("acme-reasoner-9", warnings[0], StringComparison.Ordinal);
        Assert.Contains("acme-other-1", warnings[1], StringComparison.Ordinal);
    }
}
