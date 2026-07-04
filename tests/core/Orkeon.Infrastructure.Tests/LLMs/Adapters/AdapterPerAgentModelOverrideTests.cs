using Microsoft.Extensions.AI;
using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Adapters;

namespace Orkeon.Infrastructure.Tests.LLMs.Adapters;

/// <summary>
/// Regression coverage for Experiment 07 friction #7: when the orchestrator sets
/// <see cref="ChatOptions.ModelId"/> for a per-agent LLM override (or a per-agent thinking
/// block via AdditionalProperties), the chat client adapter must forward the override to
/// the underlying provider — otherwise the model and thinking-mode hint are silently dropped.
/// </summary>
public class AdapterPerAgentModelOverrideTests
{
    [Fact]
    public async Task PerCall_ModelIdOverride_ReachesTheProvider()
    {
        var capturingProvider = new CapturingLlmProvider();
        using var adapter = new LlmProviderToChatClientAdapter(
            capturingProvider,
            LlmConfig.Create("deepseek-v4-flash"));

        var options = new ChatOptions { ModelId = "deepseek-v4-pro" };
        await adapter.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hello") }, options, CancellationToken.None);

        Assert.NotNull(capturingProvider.LastConfig);
        Assert.Equal("deepseek-v4-pro", capturingProvider.LastConfig!.Model);
    }

    [Fact]
    public async Task PerCall_ThinkingOverride_ReachesTheProvider()
    {
        var capturingProvider = new CapturingLlmProvider();
        using var adapter = new LlmProviderToChatClientAdapter(
            capturingProvider,
            LlmConfig.Create("deepseek-v4-flash"));

        var options = new ChatOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [LlmChatOptionsKeys.Thinking] = new LlmThinkingConfig { Enabled = true, Effort = "max" },
            }
        };

        await adapter.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hello") }, options, CancellationToken.None);

        Assert.NotNull(capturingProvider.LastConfig!.Thinking);
        Assert.Equal(true, capturingProvider.LastConfig.Thinking!.Enabled);
        Assert.Equal("max", capturingProvider.LastConfig.Thinking.Effort);
    }

    [Fact]
    public async Task PerCall_NoModelIdOverride_PreservesBaseModel()
    {
        var capturingProvider = new CapturingLlmProvider();
        using var adapter = new LlmProviderToChatClientAdapter(
            capturingProvider,
            LlmConfig.Create("deepseek-v4-flash"));

        // ChatOptions carries a Temperature override but NOT a ModelId — the base model
        // must be preserved.
        var options = new ChatOptions { Temperature = 0.1f };
        await adapter.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hello") }, options, CancellationToken.None);

        Assert.Equal("deepseek-v4-flash", capturingProvider.LastConfig!.Model);
        Assert.Equal(0.1, capturingProvider.LastConfig.Temperature, precision: 3);
    }

    private sealed class CapturingLlmProvider : ILlmProvider
    {
        public LlmConfig? LastConfig { get; private set; }

        public string Name => "capturing";

        public Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            LastConfig = config;
            return Task.FromResult(new LlmResponse { Content = "ok" });
        }

        public Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            LastConfig = config;
            return Task.FromResult(new LlmResponse { Content = "ok" });
        }
    }
}
