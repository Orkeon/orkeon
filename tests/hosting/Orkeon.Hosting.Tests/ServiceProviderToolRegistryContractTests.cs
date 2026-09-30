using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// Contract tests for <see cref="ServiceProviderToolRegistry"/> — the DI-backed
/// IToolRegistry the runner host registers, using hand-written doubles
/// (<see cref="StubBaseTool"/>, <see cref="FakeTool"/>) per project convention.
/// </summary>
public class ServiceProviderToolRegistryContractTests
{
    private static ServiceProviderToolRegistry CreateRegistry(params string[] toolNames)
        => new(toolNames.Select(n => new StubBaseTool(n)));

    [Fact]
    public async Task Seeded_tools_are_resolvable_by_name()
    {
        var registry = CreateRegistry("file_read", "web_scrape");

        var tool = await registry.GetToolByNameAsync("file_read");

        Assert.NotNull(tool);
        Assert.Equal("file_read", tool.Name);
    }

    [Fact]
    public async Task Name_lookup_is_case_insensitive()
    {
        var registry = CreateRegistry("file_read");

        var tool = await registry.GetToolByNameAsync("FILE_READ");

        Assert.NotNull(tool);
    }

    [Fact]
    public async Task Unknown_name_resolves_to_null()
    {
        var registry = CreateRegistry("file_read");

        Assert.Null(await registry.GetToolByNameAsync("nope"));
    }

    [Fact]
    public async Task RegisterToolAsync_refuses_a_name_held_by_another_instance()
    {
        var registry = CreateRegistry();
        var first = new StubBaseTool("dup");
        var second = new StubBaseTool("DUP");

        Assert.True(await registry.RegisterToolAsync(first));
        Assert.False(await registry.RegisterToolAsync(second));

        Assert.Same(first, await registry.GetToolAsync("dup"));
        Assert.Single(await registry.GetAllToolsAsync());
    }

    [Fact]
    public async Task RegisterToolAsync_refuses_to_shadow_a_seeded_tool()
    {
        var seeded = new StubBaseTool("file_read");
        var registry = new ServiceProviderToolRegistry([seeded]);

        Assert.False(await registry.RegisterToolAsync(new FakeTool("file_read")));

        Assert.Same(seeded, await registry.GetToolByNameAsync("file_read"));
    }

    [Fact]
    public async Task RegisterToolAsync_same_instance_twice_is_an_idempotent_success()
    {
        var registry = CreateRegistry();
        var tool = new StubBaseTool("again");

        Assert.True(await registry.RegisterToolAsync(tool));
        Assert.True(await registry.RegisterToolAsync(tool));

        Assert.Same(tool, await registry.GetToolAsync("again"));
    }

    [Fact]
    public void Two_DI_tools_with_one_name_fail_with_the_name_and_both_types()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new ServiceProviderToolRegistry([new StubBaseTool("file_read"), new FakeTool("FILE_READ")]));

        Assert.Contains("file_read", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(typeof(StubBaseTool).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(FakeTool).FullName!, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnregisterToolAsync_removes_tool()
    {
        var registry = CreateRegistry("gone");

        Assert.True(await registry.UnregisterToolAsync("gone"));
        Assert.False(await registry.IsRegisteredAsync("gone"));
        Assert.False(await registry.UnregisterToolAsync("gone"));
    }

    [Fact]
    public async Task GetAllToolsAsync_returns_every_seeded_tool()
    {
        var registry = CreateRegistry("a", "b", "c");

        var all = await registry.GetAllToolsAsync();

        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task ClearAsync_empties_the_registry()
    {
        var registry = CreateRegistry("a", "b");

        await registry.ClearAsync();

        Assert.Empty(await registry.GetAllToolsAsync());
    }
}
