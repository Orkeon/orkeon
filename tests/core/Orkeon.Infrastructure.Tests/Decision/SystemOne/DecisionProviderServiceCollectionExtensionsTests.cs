using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Infrastructure.Decision.SystemOne;

namespace Orkeon.Infrastructure.Tests.Decision.SystemOne;

public class DecisionProviderServiceCollectionExtensionsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Theory]
    [InlineData("liquid")]
    [InlineData("kev")]
    [Trait("BR", "BR-01")]
    [Trait("BR", "UC-01")]
    [Trait("BR", "UC-02")]
    public void ShouldRegisterTheChoiceAndTheModel_WhenAProviderIsNamed(string name)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddOrkeonDecisionProvider(Config(("Orkeon:Decision:Provider", name)));

        using var provider = services.BuildServiceProvider();
        var choice = provider.GetRequiredService<DecisionProviderChoice>();
        Assert.True(choice.HasProvider);
        var model = Assert.Single(provider.GetServices<IDecisionModel>());
        Assert.Equal(name, model.Name);
    }

    [Fact]
    [Trait("BR", "BR-04")]
    public void ShouldPropagateTheRefusal_WhenTwoHostedSectionsArePresent()
    {
        var services = new ServiceCollection();
        var configuration = Config(("Orkeon:TypeSafe", null), ("Orkeon:Liquid", null));

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddOrkeonDecisionProvider(configuration));

        Assert.Contains("Orkeon:Decision:Provider", ex.Message);
    }
}
