using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tools;

namespace Orkeon.Infrastructure.Tests.Tools;

/// <summary>FWK-10: ReplaceToolAsync swaps the instance registered under a name in one step.</summary>
public sealed class ToolRegistryReplaceTests
{
    [Fact]
    public async Task ShouldSwapTheInstance_WhenTheCurrentOneIsRegistered()
    {
        var current = new MockTool("t");
        var replacement = new MockTool("t");
        var registry = new ToolRegistry([current]);

        var swapped = await registry.ReplaceToolAsync(current, replacement);

        Assert.True(swapped);
        Assert.Same(replacement, await registry.GetToolByNameAsync("t"));
    }

    [Fact]
    public async Task ShouldLeaveTheName_WhenAnotherInstanceHoldsIt()
    {
        var holder = new MockTool("t");
        var registry = new ToolRegistry([holder]);

        var swapped = await registry.ReplaceToolAsync(new MockTool("t"), new MockTool("t"));

        Assert.False(swapped);
        Assert.Same(holder, await registry.GetToolByNameAsync("t"));
    }

    [Fact]
    public async Task ShouldRefuse_WhenTheReplacementCarriesAnotherName()
    {
        var current = new MockTool("t");
        var registry = new ToolRegistry([current]);

        await Assert.ThrowsAsync<ArgumentException>(() => registry.ReplaceToolAsync(current, new MockTool("other")));
    }
}
