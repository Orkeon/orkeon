using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.DependencyInjection;

namespace Orkeon.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// GAP-40, decision 2 — <c>AddOrkeonInfrastructure()</c> registers every section it binds with
/// <c>ValidateOnStart</c>: what a starting host runs (<see cref="IStartupValidator"/>, which
/// <c>IHost.StartAsync</c> calls) refuses a value the binder cannot convert, naming its key. Each
/// section is declared once, with its options, for the shipped hosts' start validation; a container
/// built without configuration keeps the defaults.
/// </summary>
public sealed class SettingsStartValidationTests
{
    private static ServiceProvider Container(params (string Key, string Value)[] values)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build());
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("RateLimiting:GlobalRequestsPerMinute", "sixty")]
    [InlineData("Orkeon:Guardian:Enabled", "oui")]
    [InlineData("Orkeon:CrewMemory:RecallLimit", "all")]
    public void The_start_validator_refuses_a_value_the_binder_cannot_convert(string key, string value)
    {
        using var container = Container((key, value));

        var error = Assert.Throws<InvalidOperationException>(() => container.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_start_validator_refuses_a_rule_of_a_section()
    {
        using var container = Container(("Memory:Provider", "redsi"));

        var error = Assert.Throws<OptionsValidationException>(() => container.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("Memory:Provider", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Readable_settings_pass_the_start_validator()
    {
        using var container = Container(("RateLimiting:GlobalRequestsPerMinute", "60"), ("Memory:Provider", "sqlite"));

        container.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void A_section_is_declared_once_with_its_options()
    {
        var services = new ServiceCollection();
        services.AddOrkeonInfrastructure();
        services.AddOrkeonSettings<GuardianOptions>("Orkeon:Guardian");

        var guardian = Assert.Single(services, descriptor =>
            descriptor.ImplementationInstance is SettingsDeclaration { Path: "Orkeon:Guardian" });
        Assert.NotNull(((SettingsDeclaration)guardian.ImplementationInstance!).Evaluate);
    }

    [Fact]
    public void A_container_without_configuration_keeps_the_defaults()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrkeonInfrastructure();
        using var container = services.BuildServiceProvider();

        Assert.Equal(new RateLimitingOptions().GlobalRequestsPerMinute,
            container.GetRequiredService<IOptions<RateLimitingOptions>>().Value.GlobalRequestsPerMinute);
    }
}
