using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Decision;

public class DecisionCircuitRegistrationTests
{
    private static bool Enabled(bool withModel, string? setting)
    {
        var values = new Dictionary<string, string?>();
        if (setting is not null)
            values["Orkeon:Decision:Enabled"] = setting;
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        if (withModel)
            services.AddSingleton<IDecisionModel>(new FakeDecisionModel());
        services.AddOrkeonGuardian();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<DecisionCircuit>().Enabled;
    }

    [Fact]
    [Trait("BR", "BR-12")]
    public void ShouldBuildCircuitOff_WhenNoModelRegisteredOrSettingDisabled()
    {
        Assert.False(Enabled(withModel: false, setting: null));
        Assert.False(Enabled(withModel: true, setting: "false"));
        Assert.True(Enabled(withModel: true, setting: null));
    }
}
