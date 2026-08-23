using Orkeon.Studio.Core.Llm;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>In-memory <see cref="IApiKeyStore"/> — records what would land in the user environment.</summary>
public sealed class FakeApiKeyStore : IApiKeyStore
{
    /// <summary>Everything saved, by environment-variable name.</summary>
    public Dictionary<string, string> Saved { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string? Peek(string envName) => Saved.GetValueOrDefault(envName);

    /// <inheritdoc />
    public void Save(string envName, string value) => Saved[envName] = value;
}
