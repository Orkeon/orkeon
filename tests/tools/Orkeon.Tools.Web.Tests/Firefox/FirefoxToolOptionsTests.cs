using Microsoft.Extensions.Configuration;

namespace Orkeon.Tools.Web.Tests.Firefox;

public class FirefoxToolOptionsTests
{
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

    [Fact]
    public void Should_KeepTheDefault_When_AValueIsUnreadableOrNotPositive()
    {
        var options = FirefoxToolOptions.From(Configuration(
            ("ExecutablePath", "  "),
            ("Headless", "maybe"),
            ("Lifetime", "forever"),
            ("StartupTimeoutSeconds", "soon"),
            ("CommandTimeoutSeconds", "0"),
            ("MaxNodes", "-3")));

        Assert.Null(options.ExecutablePath);
        Assert.False(options.Headless);
        Assert.Equal(FirefoxBrowserLifetime.Run, options.Lifetime);
        Assert.Equal(30, options.StartupTimeoutSeconds);
        Assert.Equal(30, options.CommandTimeoutSeconds);
        Assert.Equal(40, options.MaxNodes);
    }
}
