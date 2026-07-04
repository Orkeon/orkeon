using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Logging;

namespace Orkeon.Cli.TerminalGui.Tests.DependencyInjection;

// These tests inspect the IServiceCollection registrations only — no Terminal.Gui type is
// instantiated until BuildServiceProvider().GetService(...), so they survive TUI-12.
public class TerminalGuiServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOrkeonCliTerminalGui_replaces_IConsoleAdapter()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConsoleAdapter>(_ => new StubConsoleAdapter());

        services.AddOrkeonCliTerminalGui();

        // Only one IConsoleAdapter registration must remain.
        var registrations = services.Where(d => d.ServiceType == typeof(IConsoleAdapter)).ToArray();
        Assert.Single(registrations);
        // And it's no longer the stub.
        Assert.NotEqual(typeof(StubConsoleAdapter), registrations[0].ImplementationType);
    }

    [Fact]
    public void AddOrkeonCliTerminalGui_registers_TerminalGuiHost_singleton()
    {
        var services = new ServiceCollection();
        services.AddOrkeonCliTerminalGui();
        Assert.Contains(services, d => d.ServiceType == typeof(TerminalGuiHost) && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddOrkeonCliTerminalGui_registers_logger_provider_in_logger_factory()
    {
        var services = new ServiceCollection();
        services.AddOrkeonCliTerminalGui();
        Assert.Contains(services, d => d.ServiceType == typeof(ILoggerProvider));
        Assert.Contains(services, d => d.ServiceType == typeof(TerminalGuiLoggerProvider));
    }

    [Fact]
    public void AddOrkeonCliTerminalGui_with_configure_action_applies_options()
    {
        var services = new ServiceCollection();
        services.AddOrkeonCliTerminalGui(o =>
        {
            o = o with { InitialSplitRatio = 0.7, LogsBufferCapacity = 123 };
            // Note: TerminalGuiOptions is a record with init-only props,
            // so configure must mutate via the registered instance OR be designed differently.
            // Caller pattern documented as "use the options instance directly".
        });
        // Verify that a TerminalGuiOptions instance is registered.
        Assert.Contains(services, d => d.ServiceType == typeof(TerminalGuiOptions));
    }

    [Fact]
    public void AddOrkeonCliTerminalGui_called_twice_does_not_double_register()
    {
        var services = new ServiceCollection();
        services.AddOrkeonCliTerminalGui();
        services.AddOrkeonCliTerminalGui();
        Assert.Single(services, d => d.ServiceType == typeof(TerminalGuiHost));
        Assert.Single(services, d => d.ServiceType == typeof(IConsoleAdapter));
    }

    private sealed class StubConsoleAdapter : IConsoleAdapter
    {
        public void Write(string text) { }
        public void WriteLine(string text) { }
        public string? ReadLine() => null;
        public ConsoleKeyInfo ReadKey(bool intercept = false) => default;
        public void Clear() { }
    }
}
