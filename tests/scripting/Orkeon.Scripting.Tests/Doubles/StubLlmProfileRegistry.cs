using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>An <see cref="ILlmProfileRegistry"/> that knows profile names only: a pick checks names, never resolves one.</summary>
internal sealed class StubLlmProfileRegistry(params string[] names) : ILlmProfileRegistry
{
    public IReadOnlyList<string> Names { get; } = names;

    public bool IsKnown(string name) =>
        LlmProfiles.IsDefault(name) || Names.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);

    public LlmProfile Resolve(string? name) => throw new InvalidOperationException("a pick never resolves a profile");

    public LlmProfile ForProvider(ILlmProvider provider) => throw new InvalidOperationException("a pick never resolves a profile");
}
