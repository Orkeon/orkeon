using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.ConsoleApp.Services;
using Orkeon.Constants.Cli;
using Orkeon.Infrastructure.Session;

namespace Orkeon.ConsoleApp.Tests.Services;

/// <summary>
/// The permission mode is shared state between the host and a product's scripted commands: a
/// <c>/mode</c> command writes it through <c>session_store</c>, the console shows it and cycles it on
/// Shift+Tab. The key is a contract; renaming either side would break the other silently.
/// </summary>
public sealed class TuiFidelityWiringPermissionModeTests
{
    [Fact]
    public void ShiftTab_cycles_the_mode_a_scripted_command_wrote_under_the_contract_key()
    {
        Assert.Equal("default_permission_mode", SessionStateKeys.DefaultPermissionMode);
        var buffer = new InMemorySessionBufferService();
        var integration = new TuiIntegration();
        using var services = new ServiceCollection()
            .AddSingleton(integration)
            .AddSingleton<ISessionBufferService>(buffer)
            .BuildServiceProvider();
        TuiFidelityWiring.Wire(services);

        // What a /mode command writes with session_store's set_state.
        buffer.SetState("default_permission_mode", "acceptEdits");
        Assert.Equal("acceptEdits", integration.PermissionMode!());

        integration.CyclePermissionMode!();

        Assert.Equal("plan", buffer.GetState("default_permission_mode"));
        Assert.Equal("plan", integration.PermissionMode!());
    }
}
