using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Extensions;

namespace Orkeon.Infrastructure.Tests.LLMs.Extensions;

/// <summary>
/// Verifies the call-time overloads fuse the <see cref="LlmConfigOverride"/> over the
/// supplied <see cref="LlmConfig"/> via <see cref="LlmConfigResolver"/> and then call
/// the underlying provider with the effective config (priority: override &gt; base).
/// </summary>
public class LlmProviderExtensionsTests
{
    [Fact]
    public async Task GenerateAsync_OverloadFusesOverride_BeforeDelegating()
    {
        var captured = new CapturingProvider();
        var baseCfg = LlmConfig.Default() with { Temperature = 0.7 };
        var overrides = LlmConfigOverride.ForResponseFormat(LlmResponseFormat.JsonObject());

        await captured.GenerateAsync("p", overrides, baseCfg, TestContext.Current.CancellationToken);

        Assert.NotNull(captured.LastConfig);
        Assert.Equal("json_object", captured.LastConfig!.ResponseFormat!.Type);
        Assert.Equal(0.7, captured.LastConfig.Temperature);
        Assert.Equal("p", captured.LastPrompt);
    }

    [Fact]
    public async Task ChatAsync_OverloadFusesOverride_BeforeDelegating()
    {
        var captured = new CapturingProvider();
        var baseCfg = LlmConfig.Default() with { MaxTokens = 4096 };
        var overrides = new LlmConfigOverride { Temperature = 0.0, MaxTokens = 256 };

        var msgs = new[] { new LlmMessage { Role = "user", Content = "hi" } };
        await captured.ChatAsync(msgs, overrides, baseCfg, TestContext.Current.CancellationToken);

        Assert.NotNull(captured.LastConfig);
        Assert.Equal(0.0, captured.LastConfig!.Temperature);
        Assert.Equal(256, captured.LastConfig.MaxTokens);
        Assert.Same(msgs, captured.LastMessages);
    }

    [Fact]
    public async Task GenerateAsync_Overload_ThrowsArgumentNullException_OnNullOverrides()
    {
        var captured = new CapturingProvider();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            captured.GenerateAsync("p", overrides: null!, baseConfig: LlmConfig.Default(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GenerateAsync_Overload_ThrowsArgumentNullException_OnNullBaseConfig()
    {
        var captured = new CapturingProvider();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            captured.GenerateAsync("p", overrides: new LlmConfigOverride(), baseConfig: null!, TestContext.Current.CancellationToken));
    }

    private sealed class CapturingProvider : ILlmProvider
    {
        public string Name => "capturing";
        public LlmConfig? LastConfig { get; private set; }
        public string? LastPrompt { get; private set; }
        public LlmMessage[]? LastMessages { get; private set; }

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            LastPrompt = prompt;
            LastConfig = config;
            return Task.FromResult(new LlmResponse { Content = "ok" });
        }

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            LastMessages = messages;
            LastConfig = config;
            return Task.FromResult(new LlmResponse { Content = "ok" });
        }
    }
}
