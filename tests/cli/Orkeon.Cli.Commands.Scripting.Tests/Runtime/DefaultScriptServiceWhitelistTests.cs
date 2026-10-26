using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.Cli.Abstractions.Tests.Doubles;
using Orkeon.Cli.Commands.Scripting.Tests.Doubles;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>
/// R2.6 / SEC-009: the default service whitelist must NOT expose the raw
/// <see cref="IConfiguration"/> (which carries API keys / connection strings) to
/// untrusted <c>.ork.ts</c> scripts via <c>ctx.services.get('configuration')</c>.
/// </summary>
public sealed class DefaultScriptServiceWhitelistTests
{
    // GAP-13: the reserved ConfigurationKey constant is gone (no host ever served
    // it); the key a script would ask for is spelled out here.
    private const string ConfigurationKey = "configuration";

    [Fact]
    public void Default_whitelist_does_not_expose_raw_configuration()
    {
        var built = DefaultScriptServiceWhitelist.Build().Build();

        Assert.False(
            built.Entries.ContainsKey(ConfigurationKey),
            "raw IConfiguration must not be in the default whitelist (would leak secrets to scripts)");
    }

    [Fact]
    public void Default_whitelist_still_exposes_fs_and_tools()
    {
        // Non-regression: removing configuration must not drop the other canonical entries.
        var built = DefaultScriptServiceWhitelist.Build().Build();

        Assert.True(built.Entries.ContainsKey(ScriptServiceKeys.FileSystem));
        Assert.True(built.Entries.ContainsKey(ScriptServiceKeys.Tools));
    }

    [Fact]
    public void Default_whitelist_exposes_approvals_as_an_optional_entry()
    {
        var built = DefaultScriptServiceWhitelist.Build().Build();

        Assert.True(built.Entries.ContainsKey(ScriptServiceKeys.Approvals));
        Assert.Contains(ScriptServiceKeys.Approvals, built.Optional);
    }

    [Fact]
    public void Approvals_resolve_to_the_read_only_view_only_when_the_host_watches_them()
    {
        using var bare = new ServiceCollection().BuildServiceProvider();
        var absent = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), bare);
        Assert.False(absent.has(ScriptServiceKeys.Approvals));

        var watch = new FakeToolApprovalWatch();
        watch.Pending.Add(new PendingToolApproval("shell_command", "{\"command\":\"npm test\"}", "t1", "dev-batches"));
        using var watched = new ServiceCollection().AddSingleton<IToolApprovalWatch>(watch).BuildServiceProvider();
        var present = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), watched);

        Assert.True(present.has(ScriptServiceKeys.Approvals));
        var approvals = Assert.IsType<ScriptPendingApprovals>(present.get(ScriptServiceKeys.Approvals));
        var approval = Assert.Single(approvals.list());
        Assert.Equal("t1", approval.taskId);
        Assert.Equal("dev-batches", approval.command);
        Assert.Equal("shell_command", approval.tool);
        Assert.Equal("{\"command\":\"npm test\"}", approval.input);

        watch.Pending.Clear();
        Assert.Empty(approvals.list());
    }

    [Fact]
    public void Configuration_key_is_not_resolvable_even_when_configuration_is_registered()
    {
        // Even with IConfiguration present in DI, a script asking for 'configuration'
        // gets nothing — the key is simply not whitelisted.
        var sc = new ServiceCollection();
        sc.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = "sk-super-secret",
            })
            .Build());
        using var sp = sc.BuildServiceProvider();

        var locator = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), sp);

        Assert.False(locator.has(ConfigurationKey));
        Assert.Throws<InvalidOperationException>(() => locator.get(ConfigurationKey));
    }
}
