using Orkeon.Studio.Core.Llm;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>In-memory <see cref="IApiKeyStore"/> — records what would land in the user environment.</summary>
public sealed class FakeApiKeyStore : IApiKeyStore
{
    /// <summary>Everything saved, by environment-variable name.</summary>
    public Dictionary<string, string> Saved { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// When set, the persistent (user-scope) half of a save fails with it: the key is still in
    /// place for the session, as the real store's process write comes first (STUDIO-44).
    /// </summary>
    public Exception? PersistFailure { get; set; }

    /// <summary>Every variable peeked at, in order: the real store reads the registry for one the process does not hold.</summary>
    public List<string> Peeks { get; } = [];

    /// <inheritdoc />
    public string? Peek(string envName)
    {
        Peeks.Add(envName);
        return Saved.GetValueOrDefault(envName);
    }

    /// <summary>Stages a key, as a test's arrange step.</summary>
    public void Stage(string envName, string value) => Saved[envName] = value;

    /// <inheritdoc />
    public Task SaveAsync(string envName, string value)
    {
        Saved[envName] = value.Trim();
        return PersistFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }
}
