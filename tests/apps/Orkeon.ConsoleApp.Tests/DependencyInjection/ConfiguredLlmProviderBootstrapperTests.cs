using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.ConsoleApp.DependencyInjection;
using Orkeon.Domain.Constants.Llm;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>
/// <see cref="ConfiguredLlmProviderBootstrapper"/>: every documented <c>Llm:*</c> key must
/// actually reach the <see cref="LlmConfig"/> handed to the provider factory — a key the
/// docs advertise but the binder ignores fails silently (the user sets it, nothing changes).
/// </summary>
public sealed class ConfiguredLlmProviderBootstrapperTests
{
    private static LlmConfig BindAndCapture(params (string Key, string? Value)[] llmKeys)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(llmKeys.Select(k => new KeyValuePair<string, string?>(k.Key, k.Value)))
            .Build();
        var factory = new RecordingProviderFactory();

        var services = new ServiceCollection();
        services.AddSingleton<ILlmProviderFactory>(factory);
        services.AddConfiguredLlmProvider(configuration);
        using var sp = services.BuildServiceProvider();

        _ = sp.GetRequiredService<ILlmProvider>();
        Assert.NotNull(factory.LastConfig);
        return factory.LastConfig!;
    }

    [Fact]
    public void MaxRetries_is_bound_from_Llm_MaxRetries()
    {
        var config = BindAndCapture(
            ("Llm:Model", "kimi-k2"),
            ("Llm:MaxRetries", "4"));

        Assert.Equal(4, config.MaxRetries);
    }

    [Fact]
    public void MaxRetries_defaults_to_LlmDefaults_when_absent()
    {
        var config = BindAndCapture(("Llm:Model", "kimi-k2"));

        Assert.Equal(LlmDefaults.DefaultMaxRetries, config.MaxRetries);
    }

    [Fact]
    public void A_negative_MaxRetries_is_clamped_to_zero()
    {
        // Polly rejects a negative retry count at policy construction — the binder must
        // never let a bad preset crash provider creation.
        var config = BindAndCapture(
            ("Llm:Model", "kimi-k2"),
            ("Llm:MaxRetries", "-1"));

        Assert.Equal(0, config.MaxRetries);
    }

    [Fact]
    public void The_other_documented_keys_still_bind()
    {
        var config = BindAndCapture(
            ("Llm:Model", "kimi-k2"),
            ("Llm:BaseUrl", "https://api.moonshot.ai/v1"),
            ("Llm:Temperature", "0.2"),
            ("Llm:MaxTokens", "2048"),
            ("Llm:TimeoutSeconds", "90"));

        Assert.Equal("kimi-k2", config.Model);
        Assert.Equal(new Uri("https://api.moonshot.ai/v1"), config.BaseUrl);
        Assert.Equal(0.2, config.Temperature);
        Assert.Equal(2048, config.MaxTokens);
        Assert.Equal(90, config.TimeoutSeconds);
    }

    // ── doubles ──────────────────────────────────────────────────────────────

    private sealed class RecordingProviderFactory : ILlmProviderFactory
    {
        public LlmConfig? LastConfig { get; private set; }

        public IBasicLlmProvider Create(LlmConfig config)
        {
            LastConfig = config;
            return new FakeProvider();
        }
    }

    /// <summary>Both surfaces at once so the bootstrapper's ILlmProvider switch accepts it.</summary>
    private sealed class FakeProvider : ILlmProvider, IBasicLlmProvider
    {
        public string Name => "fake";

        public Task<LlmResponse> GenerateAsync(
            string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = "" });

        public Task<LlmResponse> ChatAsync(
            LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new LlmResponse { Content = "" });

        Task<string> IBasicLlmProvider.ChatAsync(
            string message, LlmConfig? config, CancellationToken cancellationToken)
            => Task.FromResult("");

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
