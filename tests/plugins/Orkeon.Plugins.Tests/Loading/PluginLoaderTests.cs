using System.Runtime.Loader;
using Orkeon.Plugins;
using Orkeon.Plugins.Tests.Doubles;
using Orkeon.Plugins.Tests.Fixtures;

namespace Orkeon.Plugins.Tests.Loading;

public class PluginLoaderTests
{
    private static PluginLoader CreateLoader() =>
        new(new OrkeonPluginsOptions().SharedAssemblyPrefixes);

    [Fact]
    public void Constructor_NullPrefixes_Throws()
    {
        Assert.Throws<ArgumentNullException>(static () => new PluginLoader(null!));
    }

    [Fact]
    public void Load_NullCandidate_Throws()
    {
        var loader = CreateLoader();

        Assert.Throws<ArgumentNullException>(() => loader.Load(null!));
    }

    [Fact]
    public void Load_FixtureAssembly_InstantiatesThePluginTheManifestNames()
    {
        using var fixture = new PluginDirectoryFixture();
        var candidate = fixture.AddFixtureCandidate();
        var loader = CreateLoader();

        var loaded = loader.Load(candidate);
        try
        {
            Assert.Equal(candidate.VirtualPath, loaded.VirtualPath);
            Assert.True(FixtureAssembly.CountPluginTypes() > 1);
            var plugin = Assert.Single(loaded.Plugins);
            Assert.Equal("fake-alpha", plugin.Name);
            Assert.Equal("1.2.3", plugin.Version);
        }
        finally
        {
            loaded.Unload();
        }
    }

    [Fact]
    public void Load_IsolatesAssembly_InACollectibleNonDefaultContext()
    {
        using var fixture = new PluginDirectoryFixture();
        var candidate = fixture.AddFixtureCandidate();
        var loader = CreateLoader();

        var loaded = loader.Load(candidate);
        try
        {
            var context = AssemblyLoadContext.GetLoadContext(loaded.Assembly);
            Assert.NotNull(context);
            Assert.NotSame(AssemblyLoadContext.Default, context);
            Assert.True(context!.IsCollectible);

            // The plugin instance types come from the isolated copy, not from the
            // host's identical types — proof the load context isolation is effective.
            var alpha = loaded.Plugins.Single(static p => p.Name == "fake-alpha");
            Assert.NotSame(typeof(FakeAlphaPlugin), alpha.GetType());
            Assert.Equal(typeof(FakeAlphaPlugin).FullName, alpha.GetType().FullName);
        }
        finally
        {
            loaded.Unload();
        }
    }

    [Fact]
    public void Load_TwoCandidates_UseDistinctLoadContexts()
    {
        using var fixture = new PluginDirectoryFixture();
        var first = fixture.AddFixtureCandidate("first");
        var second = fixture.AddFixtureCandidate("second");
        var loader = CreateLoader();

        var loadedFirst = loader.Load(first);
        var loadedSecond = loader.Load(second);
        try
        {
            Assert.NotSame(
                AssemblyLoadContext.GetLoadContext(loadedFirst.Assembly),
                AssemblyLoadContext.GetLoadContext(loadedSecond.Assembly));
            Assert.NotSame(loadedFirst.Assembly, loadedSecond.Assembly);
        }
        finally
        {
            loadedFirst.Unload();
            loadedSecond.Unload();
        }
    }

    [Fact]
    public void Load_NonPluginManagedAssembly_ThrowsWhenTheManifestNamesAPlugin()
    {
        using var fixture = new PluginDirectoryFixture();
        var candidate = fixture.CandidateFor(fixture.AddFlatNonPluginAssembly())
            with { Manifest = PluginDirectoryFixture.FixtureManifest() };
        var loader = CreateLoader();

        Assert.Throws<PluginManifestException>(() => loader.Load(candidate));
    }

    [Fact]
    public void Load_NotAManagedAssembly_ThrowsPluginLoadException_WithVirtualPathOnly()
    {
        using var fixture = new PluginDirectoryFixture();
        var candidate = fixture.CandidateFor(fixture.AddOpaqueFile("garbage.dll"))
            with { Manifest = PluginDirectoryFixture.FixtureManifest() };
        var loader = CreateLoader();

        var ex = Assert.Throws<PluginLoadException>(() => loader.Load(candidate));

        Assert.Equal(candidate.VirtualPath, ex.VirtualPath);
        Assert.Contains(candidate.VirtualPath, ex.Message, StringComparison.Ordinal);
        // VFS hygiene: the exception message must not leak the physical mount path.
        Assert.DoesNotContain(fixture.PhysicalRoot, ex.Message, StringComparison.Ordinal);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void Unload_IsIdempotent()
    {
        using var fixture = new PluginDirectoryFixture();
        var candidate = fixture.AddFixtureCandidate();
        var loaded = CreateLoader().Load(candidate);

        loaded.Unload();
        loaded.Unload(); // second call must be a no-op, not an InvalidOperationException

        Assert.True(loaded.IsUnloadRequested);
    }
}
