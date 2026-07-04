using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Scripting.Runtime;
using Orkeon.Cli.Scripting.Tests.Doubles;
using Orkeon.Domain.Tools;

namespace Orkeon.Cli.Scripting.Tests.Runtime;

/// <summary>
/// R10.4 / ANT-005: the default whitelist's <c>tools</c> entry must materialize the
/// transient (and disposable) <see cref="IBaseTool"/> set exactly once. The previous
/// resolver ran <c>sp.GetServices&lt;IBaseTool&gt;().ToArray()</c> on every get: each
/// <c>ctx.services.get("tools")</c> of a long REPL session created a fresh disposable
/// tool set retained by the root container until shutdown — unbounded memory growth.
/// These tests fail on that code.
/// </summary>
public sealed class DefaultScriptServiceWhitelistToolsCachingTests
{
    private static ServiceCollection WithCountingTool(ToolInstantiationCounter counter)
    {
        var services = new ServiceCollection();
        services.AddTransient<IBaseTool>(_ => new FakeCountingDisposableTool(counter));
        return services;
    }

    [Fact]
    public void Repeated_gets_materialize_the_transient_tools_once()
    {
        var counter = new ToolInstantiationCounter();
        using var provider = WithCountingTool(counter).BuildServiceProvider();
        var locator = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), provider);

        for (var i = 0; i < 25; i++)
            _ = locator.get(ScriptServiceKeys.Tools);

        Assert.Equal(1, counter.Count); // old code: 25 disposable sets retained by the container
    }

    [Fact]
    public void Each_get_serves_the_same_cached_instances_in_a_defensive_array()
    {
        var counter = new ToolInstantiationCounter();
        using var provider = WithCountingTool(counter).BuildServiceProvider();
        var locator = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), provider);

        var first = Assert.IsType<IBaseTool[]>(locator.get(ScriptServiceKeys.Tools));
        var second = Assert.IsType<IBaseTool[]>(locator.get(ScriptServiceKeys.Tools));

        // Same cached instance inside (old code resolved fresh transients per get)...
        Assert.Same(Assert.Single(first), Assert.Single(second));

        // ...but a fresh array shell per get: a script mutating its own array cannot
        // poison the snapshot handed to later callers.
        Assert.NotSame(first, second);
        first[0] = null!;
        var third = Assert.IsType<IBaseTool[]>(locator.get(ScriptServiceKeys.Tools));
        Assert.NotNull(third[0]);
    }

    [Fact]
    public void Concurrent_first_access_materializes_a_single_tool_set()
    {
        var counter = new ToolInstantiationCounter();
        using var provider = WithCountingTool(counter).BuildServiceProvider();
        var locator = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), provider);

        Parallel.For(0, 32, _ => locator.get(ScriptServiceKeys.Tools));

        Assert.Equal(1, counter.Count);
    }
}
