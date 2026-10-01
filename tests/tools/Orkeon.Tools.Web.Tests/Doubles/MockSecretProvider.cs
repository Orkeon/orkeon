using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Tools.Web.Tests.Doubles;

/// <summary>
/// Simple mock ISecretProvider for unit tests.
/// Returns pre-configured secrets by name.
/// </summary>
public sealed class MockSecretProvider : ISecretProvider
{
    private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

    public void AddSecret(string name, string value) => _secrets[name] = value;

    /// <summary>A provider holding only the OpenAI key <c>image_generation</c> reads.</summary>
    public static MockSecretProvider WithOpenAiKey(string value = "sk-test-key-1234567890")
    {
        var provider = new MockSecretProvider();
        provider.AddSecret(ImageGenerationTool.OpenAiApiKeySecretName, value);
        return provider;
    }

    /// <summary>Names of the secrets read so far, in order.</summary>
    public List<string> Requested { get; } = [];

    public Task<SecretValue> GetSecretAsync(string secretName, CancellationToken ct = default)
    {
        Requested.Add(secretName);
        if (_secrets.TryGetValue(secretName, out var value))
            return Task.FromResult(new SecretValue(value, "Test"));

        throw new KeyNotFoundException($"Secret '{secretName}' not found.");
    }

    public Task<bool> ExistsAsync(string secretName, CancellationToken ct = default)
        => Task.FromResult(_secrets.ContainsKey(secretName));

    public Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(_secrets.Keys.ToList().AsReadOnly());
}
