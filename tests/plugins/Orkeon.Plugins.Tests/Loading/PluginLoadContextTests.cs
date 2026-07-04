using System.Runtime.Loader;
using Orkeon.Plugins;
using Orkeon.Plugins.Tests.Fixtures;

namespace Orkeon.Plugins.Tests.Loading;

public class PluginLoadContextTests
{
    private static readonly string[] DefaultPrefixes = ["Orkeon.", "Microsoft.Extensions."];

    [Fact]
    public void Constructor_EmptyPath_ThrowsArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(
            static () => new PluginLoadContext("", DefaultPrefixes));
    }

    [Fact]
    public void Constructor_NullSharedPrefixes_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            static () => new PluginLoadContext(
                PluginDirectoryFixture.FixturePluginAssemblyPath, null!));
    }

    [Fact]
    public void Context_IsCollectible_AndNamedAfterAssembly()
    {
        var context = new PluginLoadContext(
            PluginDirectoryFixture.FixturePluginAssemblyPath, DefaultPrefixes);
        try
        {
            Assert.True(context.IsCollectible);
            Assert.NotNull(context.Name);
            Assert.StartsWith("orkeon-plugin:", context.Name, StringComparison.Ordinal);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void LoadFromAssemblyPath_LoadsIntoThisContext_NotTheDefaultOne()
    {
        using var fixture = new PluginDirectoryFixture();
        var candidate = fixture.CandidateFor(fixture.AddFlatFixturePlugin());
        var context = new PluginLoadContext(candidate.PhysicalPath, DefaultPrefixes);
        try
        {
            var assembly = context.LoadFromAssemblyPath(candidate.PhysicalPath);

            Assert.Same(context, AssemblyLoadContext.GetLoadContext(assembly));
            Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(assembly));
            // Different identity from the copy of this very assembly already loaded in the host.
            Assert.NotSame(typeof(PluginLoadContextTests).Assembly, assembly);
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void SharedPrefixes_UnifyContractTypes_WithTheHostContext()
    {
        using var fixture = new PluginDirectoryFixture();
        var candidate = fixture.CandidateFor(fixture.AddFlatFixturePlugin());
        var context = new PluginLoadContext(candidate.PhysicalPath, DefaultPrefixes);
        try
        {
            var assembly = context.LoadFromAssemblyPath(candidate.PhysicalPath);
            var pluginType = assembly.GetTypes()
                .Single(static t => t.FullName == typeof(Doubles.FakeAlphaPlugin).FullName);

            // The implementation type is isolated (per-context identity)...
            Assert.NotSame(typeof(Doubles.FakeAlphaPlugin), pluginType);
            // ...but the contract interface unified with the host's IOrkeonPlugin,
            // because "Orkeon." is a shared prefix resolved by the default context.
            Assert.True(typeof(IOrkeonPlugin).IsAssignableFrom(pluginType));
        }
        finally
        {
            context.Unload();
        }
    }
}
