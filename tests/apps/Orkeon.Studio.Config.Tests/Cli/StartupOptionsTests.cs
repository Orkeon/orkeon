using Orkeon.Studio.Config.Cli;

namespace Orkeon.Studio.Config.Tests.Cli;

public class StartupOptionsTests
{
    [Fact]
    public void No_argument_opens_the_editor()
    {
        var options = StartupOptions.Parse([]);

        Assert.False(options.IsHeadless);
        Assert.Null(options.SettingsPath);
        Assert.Null(options.Error);
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    public void Version_flag_is_headless(string flag)
    {
        var options = StartupOptions.Parse([flag]);

        Assert.True(options.ShowVersion);
        Assert.True(options.IsHeadless);
        Assert.Null(options.Error);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public void Help_flag_is_headless(string flag)
    {
        var options = StartupOptions.Parse([flag]);

        Assert.True(options.ShowHelp);
        Assert.True(options.IsHeadless);
    }

    [Fact]
    public void Settings_option_carries_its_path()
    {
        var options = StartupOptions.Parse(["--settings", "/etc/orkeon/appsettings.json"]);

        Assert.Equal("/etc/orkeon/appsettings.json", options.SettingsPath);
        Assert.False(options.IsHeadless);
    }

    [Fact]
    public void A_bare_path_is_taken_as_the_settings_file()
    {
        var options = StartupOptions.Parse(["appsettings.json"]);

        Assert.Equal("appsettings.json", options.SettingsPath);
    }

    [Fact]
    public void Settings_option_without_a_path_is_rejected()
    {
        var options = StartupOptions.Parse(["--settings"]);

        Assert.NotNull(options.Error);
        Assert.True(options.IsHeadless);
    }

    [Fact]
    public void Unknown_option_is_rejected_by_name()
    {
        var options = StartupOptions.Parse(["--nope"]);

        Assert.NotNull(options.Error);
        Assert.Contains("--nope", options.Error);
    }

    [Fact]
    public void A_second_positional_path_is_rejected()
    {
        var options = StartupOptions.Parse(["one.json", "two.json"]);

        Assert.NotNull(options.Error);
        Assert.Contains("two.json", options.Error);
    }

    [Fact]
    public void Help_text_documents_both_headless_flags()
    {
        Assert.Contains("--version", StartupOptions.HelpText);
        Assert.Contains("--help", StartupOptions.HelpText);
        Assert.Contains("--settings", StartupOptions.HelpText);
    }
}
