using Orkeon.Tools.Web.Firefox;
using Orkeon.Tools.Web.Browser;
using Microsoft.Extensions.Configuration;

namespace Orkeon.Tools.Web.Tests.Firefox;

public class FirefoxToolOptionsTests
{
    [Fact]
    public void Should_ReadTheBrowserSection_AndNameItInRefusals()
    {
        var read = FirefoxToolOptions.From(Settings(("Orkeon:Tools:Browser:Engine", " Firefox "), ("Orkeon:Tools:Browser:MaxNodes", "7")));
        var refused = Assert.Throws<InvalidOperationException>(() => FirefoxToolOptions.From(Settings(("Orkeon:Tools:Browser:Lifetime", "forever"))));

        Assert.Equal("Firefox", read.Engine);
        Assert.Equal(7, read.MaxNodes);
        Assert.Equal("Orkeon:Tools:Browser", read.Section);
        Assert.StartsWith("Orkeon:Tools:Browser:Lifetime is not a browser lifetime", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_ReadTheFormerSection_WithTheFirefoxEngineOnly()
    {
        var options = FirefoxToolOptions.From(Settings(("Orkeon:Tools:Firefox:Engine", "chrome"), ("Orkeon:Tools:Firefox:MaxNodes", "7")));

        Assert.Equal("firefox", options.Engine);
        Assert.Equal(7, options.MaxNodes);
        Assert.Equal("Orkeon:Tools:Firefox", options.Section);
    }

    [Fact]
    public void Should_RefuseBothSections()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => FirefoxToolOptions.From(Settings(
            ("Orkeon:Tools:Browser:MaxNodes", "7"), ("Orkeon:Tools:Firefox:Headless", "true"))));

        Assert.Equal("Orkeon:Tools:Browser and Orkeon:Tools:Firefox are both set: keep Orkeon:Tools:Browser, the other one is its former name.", refused.Message);
    }

    [Theory]
    [InlineData("netscape")]
    [InlineData("")]
    public void Should_RefuseAnUnknownEngine_AndNeverFallBack(string engine)
    {
        var refused = Assert.Throws<InvalidOperationException>(() => BrowserEngines.Create(new FirefoxToolOptions { Engine = engine }, null));

        Assert.Equal("Orkeon:Tools:Browser:Engine is not a browser engine: write one of firefox.", refused.Message);
        Assert.IsType<FirefoxEngine>(BrowserEngines.Create(new FirefoxToolOptions { Engine = " FIREFOX" }, null));
    }

    private static IConfiguration Settings(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    private static IConfiguration Configuration(params (string Key, string Value)[] keys) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(keys.Select(k =>
                new KeyValuePair<string, string?>($"{FirefoxToolOptions.SectionName}:{k.Key}", k.Value)))
            .Build();

    [Fact]
    public void Should_ShowTheWindowAndKeepTheDefaults_When_NothingIsConfigured()
    {
        foreach (var options in new[] { FirefoxToolOptions.From(null), FirefoxToolOptions.From(Configuration()) })
        {
            Assert.False(options.Headless);
            Assert.Null(options.ExecutablePath);
            Assert.Null(options.ProfileRoot);
            Assert.Null(options.Profile);
            Assert.Empty(options.AllowedProfiles);
            Assert.Empty(options.DeniedProfiles);
            Assert.False(options.SharedInstance);
            Assert.Equal(FirefoxBrowserLifetime.Run, options.Lifetime);
            Assert.Equal(30, options.StartupTimeoutSeconds);
            Assert.Equal(30, options.CommandTimeoutSeconds);
            Assert.Equal(40, options.MaxNodes);
            Assert.Equal(3000, options.MaxTextLength);
        }
    }

    [Fact]
    public void Should_ReadEveryKeyOfTheSection()
    {
        var options = FirefoxToolOptions.From(Configuration(
            ("ExecutablePath", " /opt/firefox/firefox "),
            ("Headless", "true"),
            ("ProfileRoot", "/var/tmp/profiles"),
            ("Profile", " work "),
            ("AllowedProfiles:0", "research"),
            ("AllowedProfiles:1", " Work "),
            ("AllowedProfiles:2", "research"),
            ("DeniedProfiles", "default; personal"),
            ("SharedInstance", "true"),
            ("Lifetime", "host"),
            ("StartupTimeoutSeconds", "90"),
            ("CommandTimeoutSeconds", "45"),
            ("MaxNodes", "25"),
            ("MaxTextLength", "1500")));

        Assert.Equal("/opt/firefox/firefox", options.ExecutablePath);
        Assert.True(options.Headless);
        Assert.Equal("/var/tmp/profiles", options.ProfileRoot);
        Assert.Equal("work", options.Profile);
        Assert.Equal(["research", "Work"], options.AllowedProfiles);
        Assert.Equal(["default", "personal"], options.DeniedProfiles);
        Assert.True(options.SharedInstance);
        Assert.Equal(FirefoxBrowserLifetime.Host, options.Lifetime);
        Assert.Equal(90, options.StartupTimeoutSeconds);
        Assert.Equal(45, options.CommandTimeoutSeconds);
        Assert.Equal(25, options.MaxNodes);
        Assert.Equal(1500, options.MaxTextLength);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("7")]
    [InlineData("Host, Run")]
    [InlineData("sk-secret-example")]
    public void Should_RefuseTheLifetime_When_ItIsNotAName(string value)
    {
        // #464: Enum.TryParse read "1" as Host, a browser kept across runs, without a word.
        var refused = Assert.Throws<InvalidOperationException>(() => FirefoxToolOptions.From(Configuration(("Lifetime", value))));

        Assert.Equal("Orkeon:Tools:Firefox:Lifetime is not a browser lifetime: write one of Run, Host.", refused.Message);
        Assert.DoesNotContain(value, refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_KeepTheDefault_When_AValueIsUnreadableOrNotPositive()
    {
        var options = FirefoxToolOptions.From(Configuration(
            ("ExecutablePath", "  "),
            ("Headless", "maybe"),
            ("StartupTimeoutSeconds", "soon"),
            ("CommandTimeoutSeconds", "0"),
            ("MaxNodes", "-3")));

        Assert.Null(options.ExecutablePath);
        Assert.False(options.Headless);
        Assert.Equal(30, options.StartupTimeoutSeconds);
        Assert.Equal(30, options.CommandTimeoutSeconds);
        Assert.Equal(40, options.MaxNodes);
    }
}
