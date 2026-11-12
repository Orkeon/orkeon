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
        Assert.Same(tool, await registry.GetToolByNameAsync("http_api", TestContext.Current.CancellationToken));
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

        var tool = await registry.GetToolByNameAsync("file_read", TestContext.Current.CancellationToken);

        Assert.NotNull(tool);
        Assert.Equal("file_read", tool.Name);
    }

    [Fact]
    public async Task Name_lookup_is_case_insensitive()
    {
        var registry = CreateRegistry("file_read");

        Assert.NotNull(await registry.GetToolByNameAsync("FILE_READ", TestContext.Current.CancellationToken));
        Assert.True(await registry.IsRegisteredAsync("File_Read", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unknown_name_resolves_to_null()
    {
        var registry = CreateRegistry("file_read");

        Assert.Null(await registry.GetToolByNameAsync("nope", TestContext.Current.CancellationToken));
        Assert.Null(await registry.GetToolAsync("nope", TestContext.Current.CancellationToken));
        Assert.False(await registry.IsRegisteredAsync("nope", TestContext.Current.CancellationToken));
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
        => await Assert.ThrowsAsync<ArgumentNullException>(() => CreateRegistry().RegisterToolAsync(null!, TestContext.Current.CancellationToken));

    // SEC-1: the delegation tools' names are theirs; an MCP server's or a plugin's tool that only carries one is refused.
    [Theory]
    [InlineData("delegate_work_to_coworker")]
    [InlineData("ASK_QUESTION_TO_COWORKER")]
    public async Task RegisterToolAsync_refuses_a_tool_that_only_carries_a_delegation_name(string name)
    {
        var registry = CreateRegistry();

        Assert.False(await registry.RegisterToolAsync(new StubBaseTool(name), TestContext.Current.CancellationToken));
        Assert.Null(await registry.GetToolByNameAsync(name, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterToolAsync_accepts_a_delegation_tool_under_its_name()
    {
        var registry = CreateRegistry();

        Assert.True(await registry.RegisterToolAsync(new StubCoworkerDelegationTool(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_seeded_tool_that_only_carries_a_delegation_name_fails_the_registry()
    {
        var error = Assert.Throws<InvalidOperationException>(() => CreateRegistry("delegate_work_to_coworker"));

        Assert.Contains("reserved", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterToolAsync_refuses_a_name_held_by_another_instance()
    {
        var registry = CreateRegistry();
        var first = new StubBaseTool("dup");
        var second = new StubBaseTool("DUP");

        Assert.True(await registry.RegisterToolAsync(first, TestContext.Current.CancellationToken));
        Assert.False(await registry.RegisterToolAsync(second, TestContext.Current.CancellationToken));

        Assert.Same(first, await registry.GetToolAsync("dup", TestContext.Current.CancellationToken));
        Assert.Single(await registry.GetAllToolsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterToolAsync_refuses_to_shadow_a_seeded_tool()
    {
        var seeded = new StubBaseTool("file_read");
        var registry = new ToolRegistry([seeded]);

        Assert.False(await registry.RegisterToolAsync(new MockTool("file_read"), TestContext.Current.CancellationToken));

        Assert.Same(seeded, await registry.GetToolByNameAsync("file_read", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterToolAsync_same_instance_twice_is_an_idempotent_success()
    {
        var registry = CreateRegistry();
        var tool = new StubBaseTool("again");

        Assert.True(await registry.RegisterToolAsync(tool, TestContext.Current.CancellationToken));
        Assert.True(await registry.RegisterToolAsync(tool, TestContext.Current.CancellationToken));

        Assert.Same(tool, await registry.GetToolAsync("again", TestContext.Current.CancellationToken));
        Assert.Single(await registry.GetAllToolsAsync(TestContext.Current.CancellationToken));
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

        Assert.Equal(50 + (4 * 250) - (4 * 84), (await registry.GetAllToolsAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task UnregisterToolAsync_removes_tool()
    {
        var registry = CreateRegistry("gone");

        Assert.True(await registry.UnregisterToolAsync("gone", TestContext.Current.CancellationToken));
        Assert.False(await registry.IsRegisteredAsync("gone", TestContext.Current.CancellationToken));
        Assert.False(await registry.UnregisterToolAsync("gone", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAllToolsAsync_returns_every_seeded_tool()
    {
        var registry = CreateRegistry("a", "b", "c");

        Assert.Equal(3, (await registry.GetAllToolsAsync(TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task ClearAsync_empties_the_registry()
    {
        var registry = CreateRegistry("a", "b");

        await registry.ClearAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await registry.GetAllToolsAsync(TestContext.Current.CancellationToken));
    }
    // SEC-4: a plugin tool dropped under ContinueOnError resolves to null in the container; the registry skips it.
    [Fact]
    public async Task Constructor_SkipsAToolTheContainerResolvedToNothing()
    {
        var registry = new ToolRegistry(new IBaseTool[] { null!, new StubBaseTool("http_api") });

        var tools = await registry.GetAllToolsAsync(TestContext.Current.CancellationToken);

        Assert.Equal("http_api", Assert.Single(tools).Name);
    }

    // ── Cancellation: every method takes the caller's token ───────

    private static CancellationToken Cancelled() => new(canceled: true);

    public static TheoryData<string> Methods() =>
    [
        nameof(IToolRegistry.RegisterToolAsync),
        nameof(IToolRegistry.UnregisterToolAsync),
        nameof(IToolRegistry.GetToolAsync),
        nameof(IToolRegistry.GetToolByNameAsync),
        nameof(IToolRegistry.GetAllToolsAsync),
        nameof(IToolRegistry.IsRegisteredAsync),
        nameof(IToolRegistry.ClearAsync),
        nameof(IToolRegistry.ReplaceToolAsync),
    ];

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task ShouldThrowOperationCanceled_WhenTheTokenIsCancelled(string method)
    {
        var tool = new MockTool("t");
        IToolRegistry registry = new ToolRegistry([tool]);
        var token = Cancelled();

        Task call = method switch
        {
            nameof(IToolRegistry.RegisterToolAsync) => registry.RegisterToolAsync(new MockTool("other"), token),
            nameof(IToolRegistry.UnregisterToolAsync) => registry.UnregisterToolAsync("t", token),
            nameof(IToolRegistry.GetToolAsync) => registry.GetToolAsync("t", token),
            nameof(IToolRegistry.GetToolByNameAsync) => registry.GetToolByNameAsync("t", token),
            nameof(IToolRegistry.GetAllToolsAsync) => registry.GetAllToolsAsync(token),
            nameof(IToolRegistry.IsRegisteredAsync) => registry.IsRegisteredAsync("t", token),
            nameof(IToolRegistry.ClearAsync) => registry.ClearAsync(token),
            _ => registry.ReplaceToolAsync(tool, new MockTool("t"), token),
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        // Nothing changed: the tool still holds its name.
        Assert.Same(tool, await registry.GetToolByNameAsync("t", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DefaultReplaceToolAsync_ShouldPassTheTokenToEveryCallItMakes()
    {
        var current = new MockTool("t");
        var registry = new FakeToolRegistry();
        await registry.RegisterToolAsync(current, TestContext.Current.CancellationToken);
        registry.ReceivedTokens.Clear();
        using var source = new CancellationTokenSource();

        var swapped = await ((IToolRegistry)registry).ReplaceToolAsync(current, new MockTool("t"), source.Token);

        Assert.True(swapped);
        Assert.Equal(
            [nameof(IToolRegistry.GetToolByNameAsync), nameof(IToolRegistry.UnregisterToolAsync), nameof(IToolRegistry.RegisterToolAsync)],
            registry.ReceivedTokens.Select(r => r.Method));
        Assert.All(registry.ReceivedTokens, r => Assert.Equal(source.Token, r.Token));
    }
}
