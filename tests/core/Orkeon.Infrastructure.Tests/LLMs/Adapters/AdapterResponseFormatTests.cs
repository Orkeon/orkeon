using Microsoft.Extensions.AI;
using Orkeon.Application.Common.DTOs;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.LLMs.Adapters;

namespace Orkeon.Infrastructure.Tests.LLMs.Adapters;

/// <summary>
/// The adapter must honor the standard <see cref="ChatOptions.ResponseFormat"/> property —
/// not only the <see cref="LlmChatOptionsKeys.ResponseFormat"/> AdditionalProperties key
/// stashed by the orchestrator. Callers built on plain Microsoft.Extensions.AI options
/// (RAG retrieval evaluator, groundedness checker, query complexity classifier) set
/// <c>ChatResponseFormat.Json</c> and expect providers wiring <c>response_format</c>
/// (e.g. DeepSeek <c>json_object</c>) to receive it.
/// </summary>
public class AdapterResponseFormatTests
{
    [Fact]
    public async Task StandardChatOptionsResponseFormatJson_ReachesTheProvider()
    {
        var capturingProvider = new CapturingLlmProvider();
        using var adapter = new LlmProviderToChatClientAdapter(
            capturingProvider,
            LlmConfig.Create("deepseek-v4-flash"));

        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.Json };
        await adapter.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hello") }, options, CancellationToken.None);

        Assert.NotNull(capturingProvider.LastConfig);
        Assert.NotNull(capturingProvider.LastConfig!.ResponseFormat);
        Assert.Equal("json_object", capturingProvider.LastConfig.ResponseFormat!.Type);
    }

    [Fact]
    public async Task AdditionalPropertiesKey_KeepsPriority_OverStandardResponseFormat()
    {
        var capturingProvider = new CapturingLlmProvider();
        using var adapter = new LlmProviderToChatClientAdapter(
            capturingProvider,
            LlmConfig.Create("deepseek-v4-flash"));

        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [LlmChatOptionsKeys.ResponseFormat] = LlmResponseFormat.Text(),
            }
        };

        await adapter.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hello") }, options, CancellationToken.None);

        Assert.Equal("text", capturingProvider.LastConfig!.ResponseFormat!.Type);
    }

    [Fact]
    public async Task NoFormatInOptions_PreservesTheBaseConfigFormat()
    {
        var capturingProvider = new CapturingLlmProvider();
        var baseConfig = LlmConfig.Create("deepseek-v4-flash") with
        {
            ResponseFormat = LlmResponseFormat.JsonObject(),
        };
        using var adapter = new LlmProviderToChatClientAdapter(capturingProvider, baseConfig);

        // Temperature forces the overrides path without carrying any response format.
        var options = new ChatOptions { Temperature = 0.1f };
        await adapter.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hello") }, options, CancellationToken.None);

        Assert.Equal("json_object", capturingProvider.LastConfig!.ResponseFormat!.Type);
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
