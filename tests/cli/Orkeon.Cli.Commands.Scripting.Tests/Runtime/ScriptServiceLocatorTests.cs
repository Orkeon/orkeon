using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.Runtime;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

public sealed class ScriptServiceLocatorTests
{
    private sealed record DummyService(string Tag);

    private static (ScriptServiceLocator Locator, ServiceProvider Services) Build(
        Action<ScriptServiceWhitelist> configure,
        Action<ServiceCollection>? configureServices = null)
    {
        var sc = new ServiceCollection();
        configureServices?.Invoke(sc);
        var sp = sc.BuildServiceProvider();
        var builder = new ScriptServiceWhitelist();
        configure(builder);
        return (new ScriptServiceLocator(builder.Build(), sp), sp);
    }

    [Fact]
    public void Get_returns_resolved_service()
    {
        var (locator, _) = Build(
            wl => wl.Add("dummy", sp => sp.GetRequiredService<DummyService>()),
            sc => sc.AddSingleton(new DummyService("hello")));

        Assert.True(locator.has("dummy"));
        var result = Assert.IsType<DummyService>(locator.get("dummy"));
        Assert.Equal("hello", result.Tag);
    }

    [Fact]
    public void Get_throws_with_listing_for_unknown_key()
    {
        var (locator, _) = Build(wl => wl.Add("dummy", sp => new DummyService("x")));

        Assert.False(locator.has("missing"));
        var ex = Assert.Throws<InvalidOperationException>(() => locator.get("missing"));
        Assert.Contains("missing", ex.Message);
        Assert.Contains("dummy", ex.Message); // available keys listed
    }

    [Fact]
    public void Optional_service_reports_false_when_unregistered()
    {
        var (locator, _) = Build(wl => wl.AddOptional("maybe", sp => sp.GetService<DummyService>()));

        Assert.False(locator.has("maybe"));
        Assert.Throws<InvalidOperationException>(() => locator.get("maybe"));
    }

    [Fact]
    public void Optional_service_reports_true_when_registered()
    {
        var (locator, _) = Build(
            wl => wl.AddOptional("maybe", sp => sp.GetService<DummyService>()),
            sc => sc.AddSingleton(new DummyService("ok")));
        Assert.True(locator.has("maybe"));
        Assert.IsType<DummyService>(locator.get("maybe"));
    }

    [Fact]
    public void Empty_locator_rejects_all_keys()
    {
        Assert.False(ScriptServiceLocator.Empty.has("anything"));
        Assert.Throws<InvalidOperationException>(() => ScriptServiceLocator.Empty.get("anything"));
    }

    [Fact]
    public void Approvals_entry_reads_the_watch_at_each_list()
    {
        var watch = new Orkeon.Cli.Abstractions.Tests.Doubles.FakeToolApprovalWatch();
        var (locator, sp) = Build(
            // The default whitelist's resolver, served under its key by this locator.
            wl => wl.AddOptional(
                ScriptServiceKeys.Approvals,
                DefaultScriptServiceWhitelist.Build().Build().Entries[ScriptServiceKeys.Approvals]),
            sc => sc.AddSingleton<Orkeon.Cli.Abstractions.Runners.IToolApprovalWatch>(watch));
        using var _ = sp;

        var approvals = Assert.IsType<ScriptPendingApprovals>(locator.get(ScriptServiceKeys.Approvals));
        Assert.Empty(approvals.list());

        watch.Pending.Add(new Orkeon.Cli.Abstractions.Runners.PendingToolApproval("read", new string('x', 80), null, null));
        var approval = Assert.Single(approvals.list());
        Assert.Null(approval.taskId);
        Assert.Null(approval.command);
        Assert.Equal("read", approval.tool);
        Assert.Equal(new string('x', 60) + "…", approval.input);
    }
}
