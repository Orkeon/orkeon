using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.HumanInput;

namespace Orkeon.Infrastructure.Tests.DependencyInjection;

public class HumanInputExtensionsTests
{
    [Fact]
    public void AddOrkeonHumanInput_RegistersToolAndProvider_Once()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddOrkeonHumanInput();

        using var sp = services.BuildServiceProvider();
        var tools = sp.GetServices<IBaseTool>().ToList();
        var humanInputTools = tools.Where(t => t.Name == "human_input").ToList();
        Assert.Single(humanInputTools);

        var provider = sp.GetService<IHumanInputProvider>();
        Assert.NotNull(provider);
        Assert.IsType<AutoApproveHumanInputProvider>(provider);
    }

    [Fact]
    public void AddOrkeonHumanInput_CalledTwice_DoesNotDuplicateTool()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddOrkeonHumanInput();
        services.AddOrkeonHumanInput();

        using var sp = services.BuildServiceProvider();
        var humanInputTools = sp.GetServices<IBaseTool>()
            .Where(t => t.Name == "human_input")
            .ToList();
        Assert.Single(humanInputTools);
    }

    [Fact]
    public void AddOrkeonHumanInput_DoesNotOverrideExistingProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSingleton<IHumanInputProvider, AutoApproveHumanInputProvider>();
        services.AddOrkeonHumanInput();

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetService<IHumanInputProvider>();
        Assert.IsType<AutoApproveHumanInputProvider>(provider);
    }
}
