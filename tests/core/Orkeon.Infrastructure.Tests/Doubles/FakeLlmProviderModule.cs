using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An <see cref="ILlmProviderModule"/> that builds a <see cref="StubLlmProvider"/> per call and keeps
/// what it was asked to build with: the configurations and the contexts, in order.
/// </summary>
public sealed class FakeLlmProviderModule : ILlmProviderModule
{
    /// <inheritdoc />
    public string Key { get; init; } = "fake-cli";

    /// <summary>The answer every provider built gives to a chat call.</summary>
    public LlmResponse ChatResponse { get; init; } = new() { Content = "from the module", TokensUsed = 12, PromptTokens = 10, CompletionTokens = 2 };

    /// <summary>The configurations the module was asked to build with.</summary>
    public List<LlmConfig> Configs { get; } = [];

    /// <summary>The contexts the module was given.</summary>
    public List<LlmProviderModuleContext> Contexts { get; } = [];

    /// <summary>The providers built, in order.</summary>
    public List<StubLlmProvider> Built { get; } = [];

    /// <inheritdoc />
    public ILlmProvider Create(LlmConfig config, LlmProviderModuleContext context)
    {
        Configs.Add(config);
        Contexts.Add(context);
        var provider = new StubLlmProvider { Name = Key }.RespondToChatWith(ChatResponse);
        Built.Add(provider);
        return provider;
    }
}
