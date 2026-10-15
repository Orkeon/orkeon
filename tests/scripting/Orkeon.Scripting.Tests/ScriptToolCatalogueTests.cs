using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.Tools;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Scripting.Tests;

/// <summary>
/// The tools a script reaches: the container's, then those the registry holds besides them (an MCP server's),
/// each name once, read again at each enumeration.
/// </summary>
public sealed class ScriptToolCatalogueTests
{
    private static ToolRegistry Registry() => new([]);

    private static string[] Names(IEnumerable<IBaseTool> tools) => [.. tools.Select(tool => tool.Name)];

    [Fact]
    public async Task ShouldHoldTheContainerToolsThenTheRegistrysOthers()
    {
        var registry = Registry();
        await registry.RegisterToolAsync(new StubBaseTool("file_read"));
        await registry.RegisterToolAsync(new StubBaseTool("echo_text"));

        var catalogue = new ScriptToolCatalogue([new StubBaseTool("file_read"), new StubBaseTool("shell_command")], registry);

        Assert.Equal(["file_read", "shell_command", "echo_text"], Names(catalogue));
    }

    [Fact]
    public void ShouldKeepTheFirstToolOfAName_WhenTheContainerHoldsTwo()
    {
        var first = new StubBaseTool("echo");
        var catalogue = new ScriptToolCatalogue([first, new StubBaseTool("ECHO")], registry: null);

        Assert.Same(first, Assert.Single(catalogue));
    }

    [Fact]
    public async Task ShouldReachATool_TheRegistryGainedAfterTheCatalogueWasMade()
    {
        var registry = Registry();
        var catalogue = new ScriptToolCatalogue([new StubBaseTool("file_read")], registry);
        Assert.Equal(["file_read"], Names(catalogue));

        await registry.RegisterToolAsync(new StubBaseTool("echo_text"));

        Assert.Equal(["file_read", "echo_text"], Names(catalogue));
    }

    [Fact]
    public void ShouldTakeTheContainerToolsOnce()
    {
        var resolved = 0;
        IEnumerable<IBaseTool> Container()
        {
            resolved++;
            yield return new StubBaseTool("file_read");
        }

        var catalogue = new ScriptToolCatalogue(Container(), registry: null);
        _ = Names(catalogue);
        _ = Names(catalogue);

        Assert.Equal(1, resolved);
    }
}
