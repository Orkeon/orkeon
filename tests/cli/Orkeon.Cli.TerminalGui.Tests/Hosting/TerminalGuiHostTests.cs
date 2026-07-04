using Orkeon.Cli.TerminalGui.Hosting;

namespace Orkeon.Cli.TerminalGui.Tests.Hosting;

// xUnit1004 suppressed: Skip is intentional. Terminal.Gui 2.1.0 ModuleInitializer crashes
// under xUnit (verified). Tracked: TUI-12.
#pragma warning disable xUnit1004 // Test methods should not be skipped

// Skip cause: Terminal.Gui 2.1.0 ModuleInitializer crashes inside xUnit test processes.
// Tracked in project/tasks: TUI-12.
public class TerminalGuiHostTests
{
    private const string SkipReason = "Terminal.Gui 2.1.0 module-init bug — see TUI-12";

    private static TerminalGuiHost CreateHost()
    {
        var options = new TerminalGuiOptions();
        return new TerminalGuiHost(options);
    }

    [Fact]
    public async Task Toplevel_throws_before_initialize()
    {
        await using var host = CreateHost();
        Assert.Throws<InvalidOperationException>(() => host.Toplevel);
    }

    [Fact]
    public async Task Initialize_is_idempotent()
    {
        await using var host = CreateHost();
        host.Initialize();
        host.Initialize();
        Assert.NotNull(host.Toplevel);
    }
}
#pragma warning restore xUnit1004
