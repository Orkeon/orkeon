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
            ("StartupTimeoutSeconds", "90"),
            ("CommandTimeoutSeconds", "45"),
            ("MaxNodes", "25"),
            ("MaxTextLength", "1500")));

        Assert.Equal("/opt/firefox/firefox", options.ExecutablePath);
        Assert.True(options.Headless);
        Assert.Equal("/var/tmp/profiles", options.ProfileRoot);
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
