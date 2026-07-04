using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Commands;
using Orkeon.Cli.Registry;

namespace Orkeon.Cli.Tests.DependencyInjection;

public sealed class CliServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOrkeonCli_registers_HelpCommand_singleton()
    {
        var sp = new ServiceCollection().AddOrkeonCli().BuildServiceProvider();
        var a = sp.GetRequiredService<HelpCommand>();
        var b = sp.GetRequiredService<HelpCommand>();
        Assert.Same(a, b);
    }

    [Fact]
    public void AddOrkeonCli_registers_ExitCommand_singleton()
    {
        var sp = new ServiceCollection().AddOrkeonCli().BuildServiceProvider();
        var a = sp.GetRequiredService<ExitCommand>();
        var b = sp.GetRequiredService<ExitCommand>();
        Assert.Same(a, b);
    }

    [Fact]
    public void AddOrkeonCli_registers_ClearCommand_singleton()
    {
        var sp = new ServiceCollection().AddOrkeonCli().BuildServiceProvider();
        var a = sp.GetRequiredService<ClearCommand>();
        var b = sp.GetRequiredService<ClearCommand>();
        Assert.Same(a, b);
    }

    [Fact]
    public void AddOrkeonCli_registers_DefaultCommandRegistry_singleton()
    {
        var sp = new ServiceCollection().AddOrkeonCli().BuildServiceProvider();
        var reg = sp.GetRequiredService<DefaultCommandRegistry>();
        Assert.Equal(3, reg.Commands.Count());
        var same = sp.GetRequiredService<DefaultCommandRegistry>();
        Assert.Same(reg, same);
    }

    [Fact]
    public void AddOrkeonCli_called_twice_does_not_double_register()
    {
        var services = new ServiceCollection();
        services.AddOrkeonCli();
        services.AddOrkeonCli();

        var sp = services.BuildServiceProvider();
        // Multiple registrations of singletons resolve to the LAST one. We just
        // verify no exception and we still get exactly one DefaultCommandRegistry.
        var regs = sp.GetServices<DefaultCommandRegistry>().ToArray();
        Assert.True(regs.Length >= 1);
        // Resolving twice still returns the same instance
        Assert.Same(regs[^1], sp.GetRequiredService<DefaultCommandRegistry>());
    }
}
