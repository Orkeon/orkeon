using System.Reflection;
using Microsoft.Extensions.Configuration;
using Orkeon.ConsoleApp.Services;

namespace Orkeon.ConsoleApp.Tests.Services;

/// <summary>
/// The banner's product line shows the built informational version (prerelease suffix kept),
/// cut at the first '+' so the build metadata never reaches the screen.
/// </summary>
public sealed class TuiFidelityWiringBannerTests
{
    private static string ExpectedVersion()
    {
        var informational = typeof(TuiFidelityWiring).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }

    private static string ProductLine() =>
        TuiFidelityWiring.BuildBannerInfo(new ConfigurationBuilder().AddInMemoryCollection().Build()).ProductLine;

    [Fact]
    [Trait("BR", "CH-01")]
    public void Banner_product_line_carries_the_informational_version()
    {
        var expected = ExpectedVersion();

        Assert.EndsWith($"orkeon-repl {expected}", ProductLine());
    }

    [Fact]
    [Trait("BR", "CH-01")]
    public void Banner_product_line_drops_the_build_metadata()
    {
        var line = ProductLine();

        Assert.DoesNotContain('+', line);
        Assert.Contains($"orkeon-repl {ExpectedVersion()}", line);
    }
}
