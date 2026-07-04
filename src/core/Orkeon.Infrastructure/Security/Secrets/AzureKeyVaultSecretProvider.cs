using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Constants.Orchestration;

namespace Orkeon.Infrastructure.Security.Secrets;

/// <summary>
/// Retrieves secrets from Azure Key Vault.
/// Uses Azure.Identity for authentication (DefaultAzureCredential).
/// Supports in-memory cache with configurable TTL.
/// </summary>
public sealed partial class AzureKeyVaultSecretProvider : ISecretProvider
{
    private readonly SecretClient _client;
    private readonly ILogger<AzureKeyVaultSecretProvider> _logger;
    private readonly ConcurrentDictionary<string, (SecretValue Value, DateTime CachedAt)> _cache = new();
    private readonly TimeSpan _cacheTtl;

    /// <summary>Initializes a new instance of <see cref="AzureKeyVaultSecretProvider"/>.</summary>
    /// <param name="vaultUri">The URI of the Azure Key Vault.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cacheTtl">Optional cache TTL. Defaults to 5 minutes.</param>
    public AzureKeyVaultSecretProvider(
        Uri vaultUri,
        ILogger<AzureKeyVaultSecretProvider> logger,
        TimeSpan? cacheTtl = null)
    {
        ArgumentNullException.ThrowIfNull(vaultUri);
        _client = new SecretClient(vaultUri, new DefaultAzureCredential());
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _cacheTtl = cacheTtl ?? OrchestrationDefaults.SecretCacheTtl;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="AzureKeyVaultSecretProvider"/> with a pre-configured client (useful for testing).
    /// </summary>
    /// <param name="client">The pre-configured Azure Key Vault secret client.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cacheTtl">Optional cache TTL. Defaults to 5 minutes.</param>
    public AzureKeyVaultSecretProvider(
        SecretClient client,
        ILogger<AzureKeyVaultSecretProvider> logger,
        TimeSpan? cacheTtl = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _cacheTtl = cacheTtl ?? OrchestrationDefaults.SecretCacheTtl;
    }

    /// <inheritdoc />
    public Task<SecretValue> GetSecretAsync(string secretName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);

        if (_cache.TryGetValue(secretName, out var cached) &&
            DateTime.UtcNow - cached.CachedAt < _cacheTtl)
        {
            return Task.FromResult(cached.Value);
        }

        return GetSecretCoreAsync(secretName, ct);

        async Task<SecretValue> GetSecretCoreAsync(string secretName, CancellationToken ct)
        {
            // Check cache again after entering async path
            if (_cache.TryGetValue(secretName, out var cached2) &&
                DateTime.UtcNow - cached2.CachedAt < _cacheTtl)
            {
                return cached2.Value;
            }

            try
            {
                var response = await _client.GetSecretAsync(secretName, cancellationToken: ct).ConfigureAwait(false);
                var kvSecret = response.Value;

                var secretValue = new SecretValue(
                    kvSecret.Value,
                    "azure-key-vault",
                    kvSecret.Properties.ExpiresOn?.UtcDateTime);

                _cache[secretName] = (secretValue, DateTime.UtcNow);

                LogSecretRetrievedFromAzureKey(secretName);
                return secretValue;
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                throw new KeyNotFoundException(
                    $"Secret '{secretName}' not found in Azure Key Vault", ex);
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string secretName, CancellationToken ct = default)
    {
        try
        {
            await _client.GetSecretAsync(secretName, cancellationToken: ct).ConfigureAwait(false);
            return true;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken ct = default)
    {
        var names = new List<string>();
        await foreach (var secret in _client.GetPropertiesOfSecretsAsync(ct).ConfigureAwait(false))
        {
            names.Add(secret.Name);
        }
        return names;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Secret {Name} retrieved from Azure Key Vault")]
    private partial void LogSecretRetrievedFromAzureKey(object name);

}
