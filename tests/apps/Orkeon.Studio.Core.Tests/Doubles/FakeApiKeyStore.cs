using Orkeon.Studio.Core.Llm;

namespace Orkeon.Studio.Core.Tests.Doubles;

/// <summary>An <see cref="IApiKeyStore"/> over a dictionary: what the user environment would hold, and nothing else.</summary>
public sealed class FakeApiKeyStore : IApiKeyStore
{
    /// <summary>The variables and their values.</summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string? Peek(string envName) => Values.GetValueOrDefault(envName);

    /// <inheritdoc />
    public void Save(string envName, string value) => Values[envName] = value;
}
