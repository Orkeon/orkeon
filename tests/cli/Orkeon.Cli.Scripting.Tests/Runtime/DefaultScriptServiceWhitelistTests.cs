using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Scripting.Runtime;

namespace Orkeon.Cli.Scripting.Tests.Runtime;

/// <summary>
/// R2.6 / SEC-009: the default service whitelist must NOT expose the raw
/// <see cref="IConfiguration"/> (which carries API keys / connection strings) to
/// untrusted <c>.ork.ts</c> scripts via <c>ctx.services.get('configuration')</c>.
/// </summary>
public sealed class DefaultScriptServiceWhitelistTests
{
    [Fact]
    public void Default_whitelist_does_not_expose_raw_configuration()
    {
        var built = DefaultScriptServiceWhitelist.Build().Build();

        Assert.False(
            built.Entries.ContainsKey(ScriptServiceKeys.Configuration),
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

        Assert.False(locator.has(ScriptServiceKeys.Configuration));
        Assert.Throws<InvalidOperationException>(() => locator.get(ScriptServiceKeys.Configuration));
    }
}
