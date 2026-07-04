using Orkeon.Plugins;

namespace Orkeon.Plugins.Tests;

public class OrkeonPluginsOptionsTests
{
    [Fact]
    public void Defaults_AreSafeAndDocumented()
    {
        var options = new OrkeonPluginsOptions();

        Assert.Equal("/plugins", options.Directory);
        Assert.Equal("*.dll", options.SearchPattern);
        Assert.False(options.ContinueOnError);
    }

    [Fact]
    public void Defaults_ShareOrkeonAndMicrosoftExtensionsAssemblies()
    {
        var options = new OrkeonPluginsOptions();

        Assert.Contains("Orkeon.", options.SharedAssemblyPrefixes);
        Assert.Contains("Microsoft.Extensions.", options.SharedAssemblyPrefixes);
    }

    [Fact]
    public void SharedAssemblyPrefixes_AreCustomizable()
    {
        var options = new OrkeonPluginsOptions();

        options.SharedAssemblyPrefixes.Add("MyCompany.Contracts");
        options.Directory = "/extensions";
        options.SearchPattern = "MyCompany.*.dll";
        options.ContinueOnError = true;

        Assert.Contains("MyCompany.Contracts", options.SharedAssemblyPrefixes);
        Assert.Equal("/extensions", options.Directory);
        Assert.Equal("MyCompany.*.dll", options.SearchPattern);
        Assert.True(options.ContinueOnError);
    }
}
