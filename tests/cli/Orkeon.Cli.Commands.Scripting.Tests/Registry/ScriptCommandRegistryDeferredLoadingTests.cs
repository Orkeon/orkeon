using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.Registry;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Commands.Scripting.Tests.Doubles;
using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Commands.Scripting.Tests.Registry;

/// <summary>
/// R10.3 / ANT-002: resolving <see cref="ScriptCommandRegistry"/> must be pure wiring —
/// script discovery, esbuild transpilation, and Jint evaluation only run at the first real
/// use (<see cref="ScriptCommandRegistry.EnsureLoadedAsync"/>). On the previous code the DI
/// singleton factory ran the whole load pipeline synchronously
/// (<c>GetAwaiter().GetResult()</c>) under the DI resolution lock, so these tests would fail.
/// </summary>
public sealed class ScriptCommandRegistryDeferredLoadingTests
{
    // ── Deferred mode (unit level, counting fake loader) ─────────────────────────

    [Fact]
    public void Construction_and_passive_reads_do_not_invoke_the_loader()
    {
        var source = new FakeScriptCommandLoadSource();

        var registry = new ScriptCommandRegistry(source.LoadAsync);

        Assert.Empty(registry.Commands);
        Assert.Equal(0, registry.Count);
        Assert.Empty(registry.ScriptCommands);
        Assert.Null(registry.Fallback);
        Assert.False(registry.IsLoaded);
        Assert.Equal(0, source.Calls); // old code: load already ran at construction
    }

    [Fact]
    public async Task First_use_triggers_the_load_exactly_once()
    {
        var source = new FakeScriptCommandLoadSource();
        var registry = new ScriptCommandRegistry(source.LoadAsync);
        var ct = TestContext.Current.CancellationToken;

        var summary = await registry.EnsureLoadedAsync(ct);
        await registry.EnsureLoadedAsync(ct);

        Assert.Equal(1, source.Calls);
        Assert.True(registry.IsLoaded);
        Assert.Equal(0, summary.Loaded);
        // The loaded snapshot is exposed: the auto-appended help-cmd helper proves delegation.
        Assert.Contains(registry.Commands, c => c.Name == "help-cmd");
    }

    [Fact]
    public async Task Concurrent_first_use_shares_a_single_load()
    {
        var source = new FakeScriptCommandLoadSource();
        var registry = new ScriptCommandRegistry(source.LoadAsync);
        var ct = TestContext.Current.CancellationToken;

        var callers = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => registry.EnsureLoadedAsync(ct), ct))
            .ToArray();
        await Task.WhenAll(callers);

        Assert.Equal(1, source.Calls);
        Assert.True(registry.IsLoaded);
    }

    [Fact]
    public async Task Failed_load_surfaces_and_is_retried_on_next_use()
    {
        var source = new FakeScriptCommandLoadSource { FailuresBeforeSuccess = 1 };
        var registry = new ScriptCommandRegistry(source.LoadAsync);
        var ct = TestContext.Current.CancellationToken;

        // Script errors must keep reaching the caller (old code: DI resolution threw).
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.EnsureLoadedAsync(ct));
        Assert.Equal(1, source.Calls);
        Assert.False(registry.IsLoaded);

        // A failed load is not memoised: the next use retries, like re-running the old factory.
        await registry.EnsureLoadedAsync(ct);
        Assert.Equal(2, source.Calls);
        Assert.True(registry.IsLoaded);
    }

    [Fact]
    public async Task Snapshot_constructor_is_loaded_up_front()
    {
        var registry = new ScriptCommandRegistry(Array.Empty<ScriptCommand>());

        Assert.True(registry.IsLoaded);
        var summary = await registry.EnsureLoadedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, summary.Loaded);
        Assert.Contains(registry.Commands, c => c.Name == "help-cmd");
    }

    // ── DI level (AddScriptCommands wiring) ──────────────────────────────────────

    [Fact]
    public void Di_resolution_does_not_construct_the_loader_pipeline()
    {
        // No IFileSystemService is registered: merely constructing ScriptCommandLoader would
        // throw. On the old code this resolution ran the whole load and failed.
        var services = new ServiceCollection();
        services.AddScriptCommands(configure: cfg => cfg.EsbuildTranspile = false);
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ScriptCommandRegistry>();

        Assert.False(registry.IsLoaded);
        Assert.Empty(registry.Commands);
    }

    [Fact]
    public async Task Di_resolution_performs_no_discovery_and_first_use_runs_it_once()
    {
        var fs = new CountingDiscoveryFileSystemService(
            new FakeFileSystemService().AddMount("/cmd"));
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystemService>(fs);
        services.AddScriptCommands(configure: cfg =>
        {
            cfg.EsbuildTranspile = false;
            cfg.Directories = ["/cmd"];
        });
        await using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ScriptCommandRegistry>();

        Assert.Equal(0, fs.EnumerateCalls); // resolution → zero load-pipeline work
        Assert.False(registry.IsLoaded);

        var summary = await registry.EnsureLoadedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, fs.EnumerateCalls); // first real use → discovery ran once
        Assert.True(registry.IsLoaded);
        Assert.Equal(0, summary.Loaded);

        await registry.EnsureLoadedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, fs.EnumerateCalls); // memoised — no re-load on later uses
    }
}
