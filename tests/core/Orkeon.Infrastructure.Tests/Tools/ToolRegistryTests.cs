using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tools;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.Tools;

/// <summary>
/// The default <see cref="IToolRegistry"/> (GAP-11): the one <c>AddOrkeonInfrastructure()</c>
/// registers, seeded from every <see cref="IBaseTool"/> in DI, so a host that references only
/// the <c>Orkeon</c> package resolves by name the tools it registers. Its contract is the one
/// the runner host's former <c>ServiceProviderToolRegistry</c> held (GAP-01): a duplicate DI
/// name fails loudly, a registered name is never overwritten.
/// </summary>
public sealed class ToolRegistryTests
{
    private static ToolRegistry CreateRegistry(params string[] toolNames)
        => new(toolNames.Select(n => new StubBaseTool(n)));

    // ── Wired by AddOrkeonInfrastructure (the NuGet consumer's path) ───────

    [Fact]
    public async Task AddOrkeonInfrastructure_resolves_a_DI_registered_tool_by_name()
    {
        var tool = new StubBaseTool("http_api");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrkeonInfrastructure();
        services.AddSingleton<IBaseTool>(tool);
        await using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IToolRegistry>();

        Assert.IsType<ToolRegistry>(registry);
        Assert.Same(tool, await registry.GetToolByNameAsync("http_api"));
    }

    [Fact]
    public async Task AddOrkeonInfrastructure_registry_is_one_singleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrkeonInfrastructure();
        await using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IToolRegistry>(), provider.GetRequiredService<IToolRegistry>());
    }

    // ── Seeding ────────────────────────────────────────────────────────────

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

        Assert.NotNull(await registry.GetToolByNameAsync("FILE_READ"));
        Assert.True(await registry.IsRegisteredAsync("File_Read"));
    }

    [Fact]
    public async Task Unknown_name_resolves_to_null()
    {
        var registry = CreateRegistry("file_read");

        Assert.Null(await registry.GetToolByNameAsync("nope"));
        Assert.Null(await registry.GetToolAsync("nope"));
        Assert.False(await registry.IsRegisteredAsync("nope"));
    }

    [Fact]
    public void Two_DI_tools_with_one_name_fail_with_the_name_and_both_types()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new ToolRegistry([new StubBaseTool("file_read"), new MockTool("FILE_READ")]));

        Assert.Contains("file_read", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(typeof(StubBaseTool).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(MockTool).FullName!, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_tool_set_is_refused()
        => Assert.Throws<ArgumentNullException>(() => new ToolRegistry(null!));

    // ── Registration at run time (MCP, plugins) ────────────────────────────

    [Fact]
    public async Task RegisterToolAsync_null_tool_throws()
        => await Assert.ThrowsAsync<ArgumentNullException>(() => CreateRegistry().RegisterToolAsync(null!));

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
        var registry = new ToolRegistry([seeded]);

        Assert.False(await registry.RegisterToolAsync(new MockTool("file_read")));

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
        Assert.Single(await registry.GetAllToolsAsync());
    }

    [Fact]
    public async Task RegisterToolAsync_while_runs_read_in_parallel_never_throws()
    {
        var registry = CreateRegistry(Enumerable.Range(0, 50).Select(i => $"seeded_{i}").ToArray());
        var ct = TestContext.Current.CancellationToken;

        // The host runs several crews at once while an MCP connection registers its tools: the
        // writes must not corrupt, or throw out of, the readers' enumeration and lookups.
        var writers = Enumerable.Range(0, 4).Select(w => Task.Run(async () =>
        {
            for (var i = 0; i < 250; i++)
            {
                var tool = new StubBaseTool($"mcp_{w}_{i}");
                Assert.True(await registry.RegisterToolAsync(tool));
                if (i % 3 == 0)
                    Assert.True(await registry.UnregisterToolAsync(tool.Name));
            }
        }, ct));

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 250; i++)
            {
                Assert.True((await registry.GetAllToolsAsync()).Count >= 50);
                Assert.NotNull(await registry.GetToolByNameAsync($"seeded_{i % 50}"));
            }
        }, ct));

        await Task.WhenAll(writers.Concat(readers));

        Assert.Equal(50 + (4 * 250) - (4 * 84), (await registry.GetAllToolsAsync()).Count);
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

        Assert.Equal(3, (await registry.GetAllToolsAsync()).Count);
    }

    [Fact]
    public async Task ClearAsync_empties_the_registry()
    {
        var registry = CreateRegistry("a", "b");

        await registry.ClearAsync();

        Assert.Empty(await registry.GetAllToolsAsync());
    }
}
