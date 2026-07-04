using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Diagnostics;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.Telemetry.HealthChecks;

/// <summary>
/// Health check that verifies LLM provider availability by calling IsAvailableAsync.
/// </summary>
public class LlmProviderHealthCheck : IHealthCheck
{
    private readonly IBasicLlmProvider _provider;

    /// <summary>Initializes a new instance of <see cref="LlmProviderHealthCheck"/>.</summary>
    /// <param name="provider">The LLM provider to check.</param>
    public LlmProviderHealthCheck(IBasicLlmProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Health-check barrier: any failure probing the LLM provider is captured and reported as a Degraded HealthCheckResult rather than propagated to the health-check host.")]
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["provider"] = _provider.Name
        };

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var isAvailable = await _provider.IsAvailableAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            data["latency_ms"] = stopwatch.ElapsedMilliseconds;

            if (isAvailable)
            {
                return HealthCheckResult.Healthy(
                    $"LLM provider '{_provider.Name}' is available (latency: {stopwatch.ElapsedMilliseconds}ms)",
                    data);
            }

            return HealthCheckResult.Degraded(
                $"LLM provider '{_provider.Name}' is unavailable",
                data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded(
                $"LLM provider '{_provider.Name}' health check failed: {ex.Message}",
                exception: ex,
                data: data);
        }
    }
}
