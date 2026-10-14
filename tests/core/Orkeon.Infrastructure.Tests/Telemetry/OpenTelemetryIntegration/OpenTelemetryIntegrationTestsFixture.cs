using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Telemetry;
using Orkeon.Infrastructure.Tests.Doubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orkeon.Infrastructure.Tests.Telemetry;

public class OpenTelemetryIntegrationTestsFixture
{
    public static ServiceCollection CreateServicesWithTelemetry(Dictionary<string, string?> configValues)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        services.AddOrkeonTelemetry(config);
        return services;
    }

    public OpenTelemetryIntegrationTestsFixture WithMockProviders(ServiceCollection services, string providerName = "Test")
    {
        var mockProvider = new MockBasicLlmProvider();
        mockProvider.Name = providerName;
        services.AddSingleton<IBasicLlmProvider>(mockProvider);

        var mockMemory = new MockMemoryProvider();
        services.AddSingleton<IMemoryProvider>(mockMemory);
        return this;
    }

    public static ServiceCollection CreateServicesWithInfrastructure(Dictionary<string, string?>? configValues = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (configValues is not null)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(configValues)
                .Build();
            services.AddOrkeonInfrastructure(config);
        }
        else
        {
            services.AddOrkeonInfrastructure();
        }

        return services;
    }
}
