using Microsoft.Extensions.AI;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Infrastructure.LLMs;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// An <see cref="ILlmProfileRegistry"/> over a fixed set of profiles, each a provider and its chat client; a null
/// or <c>default</c> name resolves the profile named <c>default</c>, and <see cref="ForProvider"/> pairs a
/// provider with <see cref="OwnClient"/>.
/// </summary>
public sealed class StubLlmProfileRegistry : ILlmProfileRegistry
{
    public Dictionary<string, LlmProfile> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The client <see cref="ForProvider"/> hands an agent that carries its own provider.</summary>
    public IChatClient OwnClient { get; set; } = new MockChatClient();

    public StubLlmProfileRegistry(params (string Name, ILlmProvider Provider, IChatClient Client)[] profiles)
    {
        foreach (var (name, provider, client) in profiles)
            Profiles[name] = Profile(name, provider, client);
    }

    public IReadOnlyList<string> Names => [.. Profiles.Keys.Where(k => !LlmProfiles.IsDefault(k))];

    public bool IsKnown(string name) => Profiles.ContainsKey(name);

    public LlmProfile Resolve(string? name) => Profiles[LlmProfiles.IsDefault(name) ? LlmProfiles.Default : name!];

    public LlmProfile ForProvider(ILlmProvider provider) => Profile("own", provider, OwnClient);

    private static LlmProfile Profile(string name, ILlmProvider provider, IChatClient client) => new()
    {
        Name = name,
        Provider = provider,
        BasicProvider = new LlmProviderAdapter(provider),
        ChatClient = client,
    };
}
