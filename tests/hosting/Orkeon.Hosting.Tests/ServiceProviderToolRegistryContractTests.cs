using Orkeon.Domain.Common;
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
    public async Task RegisterToolAsync_adds_and_overwrites_by_name()
    {
        var registry = CreateRegistry();
        var first = new StubBaseTool("dup");
        var second = new StubBaseTool("dup");

        Assert.True(await registry.RegisterToolAsync(first));
        Assert.True(await registry.RegisterToolAsync(second));

        var resolved = await registry.GetToolAsync("dup");
        Assert.Same(second, resolved);
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
    public async Task GetToolsAsync_filters_by_requested_tool_names()
    {
        var registry = CreateRegistry("file_read", "web_scrape", "csv_parse");
        var wanted = new ITool[] { new FakeTool("FILE_READ"), new FakeTool("csv_parse") };

        var resolved = await registry.GetToolsAsync(wanted);

        Assert.Equal(2, resolved.Count);
        Assert.Contains(resolved, t => t.Name == "file_read");
        Assert.Contains(resolved, t => t.Name == "csv_parse");
    }

    [Fact]
    public async Task ClearAsync_empties_the_registry()
    {
        var registry = CreateRegistry("a", "b");

        await registry.ClearAsync();

        Assert.Empty(await registry.GetAllToolsAsync());
    }
}
