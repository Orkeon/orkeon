using Microsoft.Extensions.Configuration;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Secrets;

/// <summary>
/// Resolves secrets from <see cref="IConfiguration"/> (appsettings.json, user-secrets, etc.).
/// Secrets are looked up under a configurable section (default: "Secrets").
/// For example, secret name "OpenAI" maps to configuration key "Secrets:OpenAI".
/// </summary>
public sealed class ConfigurationSecretProvider : ISecretProvider
{
    private readonly IConfiguration _configuration;
    private readonly string _sectionName;

    /// <summary>
    /// Creates a new <see cref="ConfigurationSecretProvider"/>.
    /// </summary>
    /// <param name="configuration">The configuration root to read from.</param>
    /// <param name="sectionName">The configuration section containing secrets. Default is "Secrets".</param>
    public ConfigurationSecretProvider(IConfiguration configuration, string sectionName = "Secrets")
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
        ArgumentNullException.ThrowIfNull(sectionName);
        _sectionName = sectionName;
    }

    /// <inheritdoc />
    public Task<SecretValue> GetSecretAsync(string secretName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secretName);
        ct.ThrowIfCancellationRequested();

        var key = $"{_sectionName}:{secretName}";
        var value = _configuration[key];

        if (value is null)
        {
            throw new KeyNotFoundException(
                $"Secret '{secretName}' not found. Configuration key '{key}' is not set.");
        }

        return Task.FromResult(new SecretValue(value, "Configuration"));
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string secretName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secretName);
        ct.ThrowIfCancellationRequested();

        var key = $"{_sectionName}:{secretName}";
        return Task.FromResult(_configuration[key] is not null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var section = _configuration.GetSection(_sectionName);
        var names = section.GetChildren()
            .Select(c => c.Key)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(names.AsReadOnly());
    }
}
