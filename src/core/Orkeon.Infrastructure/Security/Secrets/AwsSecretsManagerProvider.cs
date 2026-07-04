using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Infrastructure.Constants.Orchestration;

namespace Orkeon.Infrastructure.Security.Secrets;

/// <summary>
/// Retrieves secrets from AWS Secrets Manager.
/// Uses the AWS SDK with default credential chain.
/// Supports in-memory cache with configurable TTL.
/// </summary>
public sealed partial class AwsSecretsManagerProvider : ISecretProvider
{
    private readonly IAmazonSecretsManager _client;
    private readonly ILogger<AwsSecretsManagerProvider> _logger;
    private readonly ConcurrentDictionary<string, (SecretValue Value, DateTime CachedAt)> _cache = new();
    private readonly TimeSpan _cacheTtl;

    /// <summary>Initializes a new instance of <see cref="AwsSecretsManagerProvider"/>.</summary>
    /// <param name="client">The AWS Secrets Manager client.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cacheTtl">Optional cache TTL. Defaults to 5 minutes.</param>
    public AwsSecretsManagerProvider(
        IAmazonSecretsManager client,
        ILogger<AwsSecretsManagerProvider> logger,
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
                var response = await _client.GetSecretValueAsync(
                    new GetSecretValueRequest { SecretId = secretName }, ct).ConfigureAwait(false);

                var secretValue = new SecretValue(
                    response.SecretString,
                    "aws-secrets-manager");

                _cache[secretName] = (secretValue, DateTime.UtcNow);

                LogSecretRetrievedFromAwsSecrets(secretName);
                return secretValue;
            }
            catch (ResourceNotFoundException ex)
            {
                throw new KeyNotFoundException(
                    $"Secret '{secretName}' not found in AWS Secrets Manager", ex);
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string secretName, CancellationToken ct = default)
    {
        try
        {
            await _client.DescribeSecretAsync(
                new DescribeSecretRequest { SecretId = secretName }, ct).ConfigureAwait(false);
            return true;
        }
        catch (ResourceNotFoundException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken ct = default)
    {
        var names = new List<string>();
        var response = await _client.ListSecretsAsync(new ListSecretsRequest(), ct).ConfigureAwait(false);
        foreach (var secret in response.SecretList)
            names.Add(secret.Name);
        return names;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Secret {Name} retrieved from AWS Secrets Manager")]
    private partial void LogSecretRetrievedFromAwsSecrets(object name);

}
