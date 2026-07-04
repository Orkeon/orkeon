using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Telemetry;
using Orkeon.Infrastructure.Telemetry.HealthChecks;

namespace Orkeon.Infrastructure.Tests.Telemetry.HealthChecks;

public class SystemResourcesHealthCheckTestsFixture
{
    public static SystemResourcesHealthCheck CreateHealthCheck(int maxMemoryMB)
    {
        var options = Options.Create(new TelemetryOptions { MaxMemoryMB = maxMemoryMB });
        return new SystemResourcesHealthCheck(options);
    }

    public static async Task<HealthCheckResult> CheckHealthAsync(SystemResourcesHealthCheck healthCheck)
    {
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("test", healthCheck, null, null)
        };
        return await healthCheck.CheckHealthAsync(context);
    }
}
