using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Secrets;

/// <summary>
/// Resolves secrets from environment variables.
/// Variables are looked up with a configurable prefix (default: "ORKEON_").
/// For example, secret name "OPENAI_API_KEY" maps to env var "ORKEON_OPENAI_API_KEY".
/// </summary>
public sealed class EnvironmentSecretProvider : ISecretProvider
{
    private readonly string _prefix;

    /// <summary>
    /// Creates a new <see cref="EnvironmentSecretProvider"/>.
    /// </summary>
    /// <param name="prefix">Prefix prepended to secret names when looking up env vars. Default is "ORKEON_".</param>
    public EnvironmentSecretProvider(string prefix = "ORKEON_")
    {
        _prefix = prefix ?? string.Empty;
    }

    /// <inheritdoc />
    public Task<SecretValue> GetSecretAsync(string secretName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secretName);
        ct.ThrowIfCancellationRequested();

        var envVarName = _prefix + secretName.ToUpperInvariant();
        var value = Environment.GetEnvironmentVariable(envVarName);

        if (value is null)
        {
            throw new KeyNotFoundException(
                $"Secret '{secretName}' not found. Environment variable '{envVarName}' is not set.");
        }

        return Task.FromResult(new SecretValue(value, "Environment"));
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string secretName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secretName);
        ct.ThrowIfCancellationRequested();

        var envVarName = _prefix + secretName.ToUpperInvariant();
        return Task.FromResult(Environment.GetEnvironmentVariable(envVarName) is not null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var envVars = Environment.GetEnvironmentVariables();
        var names = new List<string>();

        foreach (var key in envVars.Keys)
        {
            var keyStr = key?.ToString() ?? string.Empty;
            if (keyStr.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase) && keyStr.Length > _prefix.Length)
            {
                names.Add(keyStr[_prefix.Length..]);
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(names.AsReadOnly());
    }
}
