using Microsoft.Extensions.DependencyInjection;
using Orkeon.Cli.Commands.Scripting.Runtime;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>
/// #225: a scripted command reads the version and the install channel of the runtime that runs it,
/// through <c>ctx.services.get("runtime")</c>, as <c>orkeon doctor</c> prints them.
/// </summary>
public sealed class ScriptRuntimeInfoTests
{
    [Fact]
    public void The_informational_version_is_split_into_the_release_and_the_commit()
    {
        var info = ScriptRuntimeInfo.FromInformationalVersion("1.0.0-rc.6+3ac6aa64deadbeef", "source");

        Assert.Equal("1.0.0-rc.6", info.version);
        Assert.Equal("3ac6aa64deadbeef", info.commit);
        Assert.Equal("source", info.channel);
    }

    [Fact]
    public void A_version_without_build_metadata_has_no_commit()
    {
        var info = ScriptRuntimeInfo.FromInformationalVersion("1.0.0-rc.5", "dotnet-tool");

        Assert.Equal("1.0.0-rc.5", info.version);
        Assert.Null(info.commit);
        Assert.Equal("dotnet-tool", info.channel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_version_reads_unknown(string? informational)
    {
        var info = ScriptRuntimeInfo.FromInformationalVersion(informational, "tarball");

        Assert.Equal(ScriptRuntimeInfo.Unknown, info.version);
        Assert.Null(info.commit);
    }

    [Fact]
    public void A_blank_channel_reads_unknown()
    {
        var info = ScriptRuntimeInfo.FromInformationalVersion("1.0.0", " ");

        Assert.Equal(ScriptRuntimeInfo.Unknown, info.channel);
    }

    [Fact]
    public void The_assembly_form_reads_the_informational_version_of_the_assembly()
    {
        var info = ScriptRuntimeInfo.FromAssembly(typeof(ScriptRuntimeInfo).Assembly, "deb");

        Assert.False(string.IsNullOrWhiteSpace(info.version));
        Assert.NotEqual(ScriptRuntimeInfo.Unknown, info.version);
        Assert.DoesNotContain('+', info.version);
        Assert.Equal("deb", info.channel);
    }

    [Fact]
    public void The_assembly_form_without_an_assembly_reads_unknown()
    {
        var info = ScriptRuntimeInfo.FromAssembly(null, "zip");

        Assert.Equal(ScriptRuntimeInfo.Unknown, info.version);
        Assert.Equal("zip", info.channel);
    }

    [Fact]
    public void The_default_whitelist_serves_runtime_as_a_required_entry()
    {
        var built = DefaultScriptServiceWhitelist.Build().Build();

        Assert.True(built.Entries.ContainsKey(ScriptServiceKeys.Runtime));
        Assert.DoesNotContain(ScriptServiceKeys.Runtime, built.Optional);
    }

    [Fact]
    public void Runtime_resolves_to_the_info_the_host_registered()
    {
        var registered = ScriptRuntimeInfo.FromInformationalVersion("1.0.0-rc.6+abc", "source");
        using var sp = new ServiceCollection().AddSingleton(registered).BuildServiceProvider();
        var locator = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), sp);

        Assert.True(locator.has(ScriptServiceKeys.Runtime));
        Assert.Same(registered, locator.get(ScriptServiceKeys.Runtime));
    }

    [Fact]
    public void Runtime_falls_back_to_the_entry_assembly_with_an_unknown_channel()
    {
        using var sp = new ServiceCollection().BuildServiceProvider();
        var locator = new ScriptServiceLocator(DefaultScriptServiceWhitelist.Build().Build(), sp);

        var info = Assert.IsType<ScriptRuntimeInfo>(locator.get(ScriptServiceKeys.Runtime));
        Assert.False(string.IsNullOrWhiteSpace(info.version));
        Assert.Equal(ScriptRuntimeInfo.Unknown, info.channel);
    }
}
