using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Constants.Llm;
using Orkeon.Infrastructure.Decision.TypeSafe;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The decision models of a shipped host (ADR-015, ADR-016): TypeSafe is registered when the
/// settings carry an <c>Orkeon:TypeSafe</c> section, the decision profiles always.
/// </summary>
public sealed class RunnerHostDecisionModelsTests
{
    private static ServiceCollection Register(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        var context = new HostBuilderContext(new Dictionary<object, object>()) { Configuration = configuration };
        RunnerHost.RegisterDecisionModels(context, services);
        return services;
    }

    [Fact]
    public void ShouldRegisterTheTypeSafeDecisionModel_WhenTheSettingsHoldATypeSafeSection()
    {
        var services = Register(new Dictionary<string, string?> { ["Orkeon:TypeSafe:Model"] = "test" });

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IDecisionModel));
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Contains(services, d => d.ServiceType == typeof(TypeSafeDecisionModel));
    }

    [Fact]
    [Trait("BR", "R-64")]
    public void ShouldRegisterDecisionModel_WhenTypeSafeSectionIsEmpty()
    {
        using var json = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"Orkeon\":{\"TypeSafe\":{}}}"));
        var configuration = new ConfigurationBuilder().AddJsonStream(json).Build();
        var services = new ServiceCollection();
        var context = new HostBuilderContext(new Dictionary<object, object>()) { Configuration = configuration };

        RunnerHost.RegisterDecisionModels(context, services);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IDecisionModel));
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Contains(services, d => d.ServiceType == typeof(TypeSafeDecisionModel));
    }

    [Fact]
    public void ShouldRegisterNoDecisionModel_WhenTheSettingsHoldNoTypeSafeSection()
    {
        // The key variable also opts TypeSafe in: a machine that sets it cannot prove the absence.
        Assert.SkipWhen(
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DecisionProviderDefaults.TypeSafeApiKeyVariable)),
            $"{DecisionProviderDefaults.TypeSafeApiKeyVariable} is set on this machine.");

        var services = Register(new Dictionary<string, string?> { ["Orkeon:Decision:Enabled"] = "true" });

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IDecisionModel));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(TypeSafeDecisionModel));
    }
}
