using Microsoft.Extensions.AI;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Rag.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="ILlmProfileRegistry"/>: the host's named profiles as chat clients, the
/// default one being the container's own (left to the caller). Records every
/// <see cref="Resolve"/>, so a test can tell a profile that was only checked by name from one
/// whose provider was asked for.
/// </summary>
public sealed class StubLlmProfileRegistry : ILlmProfileRegistry
{
    private readonly Dictionary<string, IChatClient> _profiles;

    /// <summary>Builds the registry over <paramref name="profiles"/>, in their order.</summary>
    public StubLlmProfileRegistry(params (string Name, IChatClient ChatClient)[] profiles)
    {
        _profiles = profiles.ToDictionary(p => p.Name, p => p.ChatClient, StringComparer.OrdinalIgnoreCase);
        Names = [.. profiles.Select(p => p.Name)];
    }

    /// <summary>The names <see cref="Resolve"/> was asked for, in order.</summary>
    public List<string?> Resolved { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<string> Names { get; }

    /// <inheritdoc />
    public bool IsKnown(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return LlmProfiles.IsDefault(name) || _profiles.ContainsKey(name.Trim());
    }

    /// <inheritdoc />
    public LlmProfile Resolve(string? name)
    {
        Resolved.Add(name);
        if (LlmProfiles.IsDefault(name))
            throw new InvalidOperationException("The stub leaves the default profile to the container's own chat client.");

        if (!_profiles.TryGetValue(name!.Trim(), out var chatClient))
            throw new InvalidOperationException(LlmProfiles.UnknownMessage(name, "A crew", Names));

        return new LlmProfile
        {
            Name = name.Trim(),
            Provider = new StubLlmProvider { Name = name.Trim() },
            BasicProvider = new UnusedBasicProvider(name.Trim()),
            ChatClient = chatClient,
        };
    }

    /// <inheritdoc />
    public LlmProfile ForProvider(Orkeon.Domain.SharedKernel.ILlmProvider provider) =>
        throw new InvalidOperationException("The RAG subsystem runs on host profiles, never on an agent's own provider.");

    /// <summary>The basic surface of a profile, which the RAG subsystem never calls.</summary>
    private sealed class UnusedBasicProvider(string name) : IBasicLlmProvider
    {
        public string Name { get; } = name;

        public Task<string> ChatAsync(
            string message, Orkeon.Domain.SharedKernel.ValueObjects.LlmConfig? config = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The RAG subsystem talks to a profile through its chat client.");

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
