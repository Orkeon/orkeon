using Microsoft.Extensions.AI;
using Orkeon.Application.Crew;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;
using Orkeon.Infrastructure.LLMs;

namespace Orkeon.Application.Tests.Doubles;

/// <summary>An <see cref="ILlmProfileRegistry"/> over a fixed set of profiles, each a provider and its chat client.</summary>
public sealed class StubLlmProfileRegistry : ILlmProfileRegistry
{
    public Dictionary<string, LlmProfile> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public StubLlmProfileRegistry(params (string Name, ILlmProvider Provider, IChatClient Client)[] profiles)
    {
        foreach (var (name, provider, client) in profiles)
            Profiles[name] = new LlmProfile
            {
                Name = name,
                Provider = provider,
                BasicProvider = new LlmProviderAdapter(provider),
                ChatClient = client,
            };
    }

    public IReadOnlyList<string> Names => [.. Profiles.Keys];

    public bool IsKnown(string name) => Profiles.ContainsKey(name);

    public LlmProfile Resolve(string? name) => Profiles[name!];

    public LlmProfile ForProvider(ILlmProvider provider) => throw new NotSupportedException();
}
