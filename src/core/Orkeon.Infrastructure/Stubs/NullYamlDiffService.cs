using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Infrastructure;
using Orkeon.Domain.Configuration;

namespace Orkeon.Infrastructure.Stubs;

/// <summary>
/// No-op implementation of <see cref="IYamlDiffService"/> that returns empty diffs.
/// Logs a warning on first use to indicate no YAML diff logic is configured.
/// </summary>
public sealed partial class NullYamlDiffService : IYamlDiffService
{
    private readonly ILogger<NullYamlDiffService> _logger;
    private int _warnedOnce;

    /// <summary>Initializes a new instance of <see cref="NullYamlDiffService"/>.</summary>
    public NullYamlDiffService(ILogger<NullYamlDiffService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    private void WarnOnce()
    {
        if (Interlocked.Exchange(ref _warnedOnce, 1) == 0)
            LogNullDiffServiceFallback();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Using NullYamlDiffService — YAML diff functionality is not available. Register a real IYamlDiffService for production.")]
    private partial void LogNullDiffServiceFallback();

    /// <inheritdoc />
    public Task<ConfigurationDiff> CompareYamlAsync(string yaml1, string yaml2, CancellationToken cancellationToken = default)
    {
        WarnOnce();
        return Task.FromResult(ConfigurationDiff.NoChanges("yaml-comparison"));
    }

    /// <inheritdoc />
    public Task<string> ApplyDiffAsync(string baseContent, ConfigurationDiff diff, CancellationToken cancellationToken = default)
    {
        WarnOnce();
        // Return unmodified content when no real diff engine is configured
        return Task.FromResult(baseContent);
    }
}
