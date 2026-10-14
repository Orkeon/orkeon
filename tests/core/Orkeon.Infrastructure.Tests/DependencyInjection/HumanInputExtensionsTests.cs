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
        Assert.IsType<DeadlinePausingHumanInputProvider>(provider);
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public async System.Threading.Tasks.Task ShouldWrapProviderRegisteredAfterHumanInput_WhenDecorationRunsLast()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrkeonHumanInput();
        var late = new Orkeon.Infrastructure.Tests.Doubles.MockHumanInputProvider();
        late.SetConfirmationResult(false);
        services.AddSingleton<IHumanInputProvider>(late);

        services.DecorateHumanInputWithDeadline();
        services.DecorateHumanInputWithDeadline();

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IHumanInputProvider>();
        Assert.IsType<DeadlinePausingHumanInputProvider>(provider);
        var context = Orkeon.Domain.HumanInput.HumanInputContext.CreateTextInput(
            Orkeon.Domain.Common.AgentId.Create(), Orkeon.Domain.Common.TaskId.Create(), "Proceed?");
        Assert.False(await provider.GetConfirmationAsync(context, TestContext.Current.CancellationToken));
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
    [Trait("BR", "CH-01")]
    public async System.Threading.Tasks.Task AddOrkeonHumanInput_DoesNotOverrideExistingProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // A refusal: the auto-approve default would answer true, so false proves whose answer it is.
        var existing = new Orkeon.Infrastructure.Tests.Doubles.MockHumanInputProvider();
        existing.SetConfirmationResult(false);
        services.AddSingleton<IHumanInputProvider>(existing);
        services.AddOrkeonHumanInput();

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetService<IHumanInputProvider>();
        Assert.IsType<DeadlinePausingHumanInputProvider>(provider);
        var context = Orkeon.Domain.HumanInput.HumanInputContext.CreateTextInput(
            Orkeon.Domain.Common.AgentId.Create(), Orkeon.Domain.Common.TaskId.Create(), "Proceed?");
        Assert.False(await provider.GetConfirmationAsync(context, TestContext.Current.CancellationToken));
    }
}
