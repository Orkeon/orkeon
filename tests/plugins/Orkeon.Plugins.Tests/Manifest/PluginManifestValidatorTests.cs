using Orkeon.Plugins;

namespace Orkeon.Plugins.Tests.Manifest;

/// <summary>The manifest's required fields, its Orkeon version range and its JSON form.</summary>
public class PluginManifestValidatorTests
{
    private const string Host = "1.0.0-rc.6";

    private static PluginManifest Valid(string range = "[1.0.0-rc.1,2.0.0)") => new()
    {
        Name = "acme.weather",
        Version = "1.0.0",
        OrkeonVersion = range,
        EntryAssembly = "Acme.Weather.dll",
        Tools = ["weather.get"],
    };

    [Fact]
    public void Validate_CompleteCompatibleManifest_DoesNotThrow()
    {
        PluginManifestValidator.Validate(Valid(), Host);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("version")]
    [InlineData("orkeonVersion")]
    [InlineData("entryAssembly")]
    public void Validate_MissingOrBlankRequiredField_ThrowsNamingTheField(string field)
    {
        foreach (var blank in new string?[] { null, "", "  " })
        {
            var manifest = field switch
            {
                "name" => Valid() with { Name = blank },
                "version" => Valid() with { Version = blank },
                "orkeonVersion" => Valid() with { OrkeonVersion = blank },
                _ => Valid() with { EntryAssembly = blank },
            };

            var ex = Assert.Throws<PluginManifestException>(() => PluginManifestValidator.Validate(manifest, Host));

            Assert.Contains(field, ex.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("../Evil.dll")]
    [InlineData("sub/Evil.dll")]
    [InlineData("sub\\Evil.dll")]
    [InlineData("Evil.exe")]
    [InlineData("Evil")]
    public void Validate_EntryAssemblyThatIsNotAPlainDllName_Throws(string entry)
    {
        var ex = Assert.Throws<PluginManifestException>(
            () => PluginManifestValidator.Validate(Valid() with { EntryAssembly = entry }, Host));

        Assert.Contains("entryAssembly", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_BlankOrDuplicateToolName_Throws()
    {
        Assert.Throws<PluginManifestException>(
            () => PluginManifestValidator.Validate(Valid() with { Tools = ["a", " "] }, Host));
        Assert.Throws<PluginManifestException>(
            () => PluginManifestValidator.Validate(Valid() with { Tools = ["a", "a"] }, Host));
    }

    [Theory]
    [InlineData("[2.0.0,)")]
    [InlineData("[1.0.0,2.0.0)")]
    [InlineData("(1.0.0-rc.6,)")]
    [InlineData("[1.0.0-rc.5]")]
    [InlineData("(,1.0.0-rc.6)")]
    [InlineData("2.0.0")]
    public void Validate_HostOutsideTheRange_ThrowsNamingBothVersions(string range)
    {
        var ex = Assert.Throws<PluginManifestException>(
            () => PluginManifestValidator.Validate(Valid(range), Host));

        Assert.Contains(range, ex.Message, StringComparison.Ordinal);
        Assert.Contains(Host, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1.0.0-rc.1")]
    [InlineData("[1.0.0-rc.6]")]
    [InlineData("[1.0.0-rc.1,2.0.0)")]
    [InlineData("(,1.0.0-rc.6]")]
    [InlineData("[1.0.0-rc.1,)")]
    [InlineData("(1.0.0-rc.5,1.0.0)")]
    [InlineData("*")]
    public void Validate_HostInsideTheRange_DoesNotThrow(string range)
    {
        PluginManifestValidator.Validate(Valid(range), Host);
    }

    [Fact]
    public void Validate_BuildMetadataOfTheHost_IsIgnored()
    {
        PluginManifestValidator.Validate(Valid("[1.0.0-rc.6]"), "1.0.0-rc.6+abcdef");
    }

    [Theory]
    [InlineData("[1.0.0")]
    [InlineData("abc")]
    [InlineData("[2.0.0,1.0.0]")]
    [InlineData("[1.0.0,2.0.0,3.0.0]")]
    public void Validate_MalformedRange_Throws(string range)
    {
        var ex = Assert.Throws<PluginManifestException>(
            () => PluginManifestValidator.Validate(Valid(range), Host));

        Assert.Contains("orkeonVersion", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ReadsEveryField()
    {
        var manifest = PluginManifest.Parse(
            """
            { "name": "acme.weather", "version": "1.0.0", "orkeonVersion": "[1.0.0-rc.1,2.0.0)",
              "entryAssembly": "Acme.Weather.dll", "tools": [ "weather.get", "weather.alerts" ], "extra": 1 }
            """);

        Assert.Equal("acme.weather", manifest.Name);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal("[1.0.0-rc.1,2.0.0)", manifest.OrkeonVersion);
        Assert.Equal("Acme.Weather.dll", manifest.EntryAssembly);
        Assert.Equal(["weather.get", "weather.alerts"], manifest.Tools);
    }

    [Fact]
    public void Parse_AbsentToolsKey_MeansNoTool()
    {
        var manifest = PluginManifest.Parse("""{ "name": "a" }""");

        Assert.Equal("a", manifest.Name);
        Assert.Empty(manifest.Tools);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "name": 3 }""")]
    [InlineData("""{ "tools": "weather.get" }""")]
    [InlineData("""{ "tools": [ 1 ] }""")]
    public void Parse_MalformedDocument_ThrowsPluginManifestException(string json)
    {
        Assert.Throws<PluginManifestException>(() => PluginManifest.Parse(json));
    }
}
