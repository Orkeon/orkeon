using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.Security.Secrets;

/// <summary>
/// Chains multiple <see cref="ISecretProvider"/> instances, trying each in order.
/// The first provider that successfully returns a secret wins.
/// Exceptions (except <see cref="OperationCanceledException"/>) are caught and logged,
/// then the next provider is tried.
/// </summary>
public sealed partial class ChainedSecretProvider : ISecretProvider
{
    private readonly IReadOnlyList<ISecretProvider> _providers;
    private readonly ILogger<ChainedSecretProvider> _logger;

    /// <summary>
    /// Creates a new <see cref="ChainedSecretProvider"/>.
    /// </summary>
    /// <param name="providers">Ordered list of providers to try.</param>
    /// <param name="logger">Logger for recording fallback attempts.</param>
    public ChainedSecretProvider(IReadOnlyList<ISecretProvider> providers, ILogger<ChainedSecretProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        if (_providers.Count == 0)
        {
            throw new ArgumentException("At least one provider is required.", nameof(providers));
        }
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Chained-provider fallback barrier: a provider that throws is logged and the next provider in the chain is tried (cancellation is rethrown).")]
    public Task<SecretValue> GetSecretAsync(string secretName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secretName);
        return GetSecretCoreAsync(secretName, ct);

        async Task<SecretValue> GetSecretCoreAsync(string secretName, CancellationToken ct)
        {
            for (var i = 0; i < _providers.Count; i++)
            {
                try
                {
                    var result = await _providers[i].GetSecretAsync(secretName, ct).ConfigureAwait(false);
                    return result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogProviderCouldNotResolveSecret(ex, i, _providers[i].GetType().Name, secretName);
                }
            }

            throw new KeyNotFoundException(
                $"Secret '{secretName}' was not found in any of the {_providers.Count} configured providers.");
        }
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Chained-provider fallback barrier: a provider that throws while checking existence is logged and the next provider in the chain is tried (cancellation is rethrown).")]
    public Task<bool> ExistsAsync(string secretName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secretName);
        return ExistsCoreAsync(secretName, ct);

        async Task<bool> ExistsCoreAsync(string secretName, CancellationToken ct)
        {
            foreach (var provider in _providers)
            {
                try
                {
                    if (await provider.ExistsAsync(secretName, ct).ConfigureAwait(false))
                        return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogProviderThrewWhileCheckingExistence(ex, provider.GetType().Name, secretName);
                }
            }

            return false;
        }
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Chained-provider aggregation barrier: a provider that throws while listing is logged and skipped so the names from the other providers are still aggregated (cancellation is rethrown).")]
    public async Task<IReadOnlyList<string>> ListSecretNamesAsync(CancellationToken ct = default)
    {
        var allNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in _providers)
        {
            try
            {
                var names = await provider.ListSecretNamesAsync(ct).ConfigureAwait(false);
                foreach (var name in names)
                    allNames.Add(name);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogProviderThrewWhileListingSecret(ex, provider.GetType().Name);
            }
        }

        return allNames.ToList().AsReadOnly();
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Provider {ProviderIndex} ({ProviderType}) could not resolve secret '{SecretName}', trying next.")]
    private partial void LogProviderCouldNotResolveSecret(Exception ex, int providerIndex, object providerType, object secretName);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Provider {ProviderType} threw while checking existence of '{SecretName}'.")]
    private partial void LogProviderThrewWhileCheckingExistence(Exception ex, object providerType, object secretName);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Provider {ProviderType} threw while listing secret names.")]
    private partial void LogProviderThrewWhileListingSecret(Exception ex, object providerType);

}
