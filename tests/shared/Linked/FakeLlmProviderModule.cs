using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Tests.Shared.Doubles;

/// <summary>
/// An <see cref="ILlmProviderModule"/> that builds a <see cref="StubLlmProvider"/> per call and keeps
/// what it was asked to build with: the configurations and the contexts, in order. One source file,
/// compiled into each test project that links it: the port is in <c>Orkeon.Application</c>, which
/// <c>Orkeon.Tests.Shared</c> does not reference.
/// </summary>
internal sealed class FakeLlmProviderModule : ILlmProviderModule
{
    private readonly Lock _gate = new();

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

    /// <summary>How many providers the module built.</summary>
    public int Builds
    {
        get
        {
            lock (_gate)
                return Built.Count;
        }
    }

    /// <inheritdoc />
    public ILlmProvider Create(LlmConfig config, LlmProviderModuleContext context)
    {
        var provider = new StubLlmProvider { Name = Key }.RespondToChatWith(ChatResponse);
        lock (_gate)
        {
            Configs.Add(config);
            Contexts.Add(context);
            Built.Add(provider);
        }

        return provider;
    }
}
