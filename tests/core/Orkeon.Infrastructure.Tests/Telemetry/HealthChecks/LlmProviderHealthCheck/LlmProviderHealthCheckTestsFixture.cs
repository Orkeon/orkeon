using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Telemetry.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Orkeon.Infrastructure.Tests.Telemetry.HealthChecks;

public class LlmProviderHealthCheckTestsFixture
{
    private readonly MockBasicLlmProvider _provider = new();

    public LlmProviderHealthCheckTestsFixture WithName(string name)
    {
        _provider.Name = name;
        return this;
    }

    public LlmProviderHealthCheckTestsFixture WithAvailability(bool isAvailable)
    {
        _provider.SetIsAvailable(isAvailable);
        return this;
    }

    public LlmProviderHealthCheckTestsFixture WithAvailabilityException(Exception exception)
    {
        _provider.SetIsAvailableException(exception);
        return this;
    }

    public LlmProviderHealthCheckTestsFixture WithAvailabilityFunc(Func<Task<bool>> func)
    {
        _provider.SetIsAvailableFunc(func);
        return this;
    }

    public LlmProviderHealthCheck CreateHealthCheck()
        => new(_provider);

    public static async Task<HealthCheckResult> CheckHealthAsync(LlmProviderHealthCheck healthCheck)
    {
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("test", healthCheck, null, null)
        };
        return await healthCheck.CheckHealthAsync(context);
    }

    public MockBasicLlmProvider GetProvider() => _provider;
}
