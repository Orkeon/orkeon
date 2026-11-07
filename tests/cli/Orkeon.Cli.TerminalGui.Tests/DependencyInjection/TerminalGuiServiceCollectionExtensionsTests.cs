using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Logging;
using Orkeon.Cli.TerminalGui.Tests.Doubles;

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
    public void AddOrkeonCliTerminalGui_called_twice_does_not_double_register()
    {
        var services = new ServiceCollection();
        services.AddOrkeonCliTerminalGui();
        services.AddOrkeonCliTerminalGui();
        Assert.Single(services, d => d.ServiceType == typeof(TerminalGuiHost));
        Assert.Single(services, d => d.ServiceType == typeof(IConsoleAdapter));
    }

    [Fact]
    public void ShouldRemoveEveryLoggerProvider_WhenClearStdoutLoggersCalled()
    {
        var builder = new StubLoggingBuilder();
        builder.Services.AddSingleton<ILoggerProvider>(_ => throw new InvalidOperationException("never built"));
        builder.Services.AddSingleton<ILoggerProvider>(_ => throw new InvalidOperationException("never built"));
        builder.Services.AddSingleton(new TerminalGuiOptions());

        var returned = builder.ClearStdoutLoggersForTerminalGui();

        Assert.Same(builder, returned);
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(ILoggerProvider));
        Assert.Single(builder.Services);
        Assert.Throws<ArgumentNullException>(() => ((ILoggingBuilder)null!).ClearStdoutLoggersForTerminalGui());
    }

    [Fact]
    public void ShouldRegisterDefaultOptions_WhenOptionsInstanceIsNull()
    {
        var services = new ServiceCollection();
        services.AddOrkeonCliTerminalGui(null);
        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(TerminalGuiOptions));
        Assert.Equal(new TerminalGuiOptions(), descriptor.ImplementationInstance);
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
