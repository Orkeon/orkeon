using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Cli.Abstractions.Console;
using Orkeon.ConsoleApp.Services;

namespace Orkeon.ConsoleApp.Tests.Services;

public sealed class TuiThemeSettingTests
{
    private const string Path = "Orkeon:Cli:Tui:Theme";

    private static ServiceProvider Holding(string? value) => new ServiceCollection()
        .AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [Path] = value })
            .Build())
        .BuildServiceProvider();

    [Theory]
    [InlineData("light", ConsoleTheme.Light)]
    [InlineData("DARK", ConsoleTheme.Dark)]
    [InlineData("None", ConsoleTheme.None)]
    public void A_palette_is_read_by_its_name_whatever_its_case(string value, ConsoleTheme expected)
    {
        // Arrange
        // Act
        var theme = TuiThemeSetting.Parse(value);

        // Assert
        Assert.Equal(expected, theme);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("Dark,Light")]
    [InlineData("sepia")]
    [InlineData("")]
    [InlineData(null)]
    public void A_value_that_names_no_palette_reads_as_none(string? value)
    {
        // Arrange
        // Act
        var theme = TuiThemeSetting.Parse(value);

        // Assert
        Assert.Null(theme);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("sepia")]
    public void A_value_that_names_no_palette_is_refused_naming_the_setting(string value)
    {
        // Arrange
        using var services = Holding(value);

        // Act
        var refused = Assert.Throws<OptionsValidationException>(() => TuiThemeSetting.Judge(services, Path));

        // Assert
        Assert.Equal($"{Path} is '{value}', which is not a palette: write one of Dark, Light, None.", Assert.Single(refused.Failures));
    }

    [Theory]
    [InlineData("light")]
    [InlineData(null)]
    public void A_palette_or_no_value_is_not_refused(string? value)
    {
        // Arrange
        using var services = Holding(value);

        // Act
        var thrown = Record.Exception(() => TuiThemeSetting.Judge(services, Path));

        // Assert
        Assert.Null(thrown);
    }
}
