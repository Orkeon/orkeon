using System.Diagnostics;
using Orkeon.Analysis.Abstractions.Interfaces;
using Orkeon.Analysis.Core.Editing;

namespace Orkeon.Analysis.Tests.Editing;

public sealed class DotnetBuildValidatorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("dotnetbuild").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    [Trait("BR", "RT-41")]
    [Trait("Category", "Slow")]
    public async Task ShouldRunRealDotnetBuild_WhenValidateBuildOnFixtureProject()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "Fixture.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            TestContext.Current.CancellationToken);
        var source = Path.Combine(_dir, "Lib.cs");
        await File.WriteAllTextAsync(source, "public class L { }\n", TestContext.Current.CancellationToken);
        using (var restore = Process.Start(new ProcessStartInfo("dotnet", ["restore", Path.Combine(_dir, "Fixture.csproj")])
        {
            WorkingDirectory = _dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!)
        {
            await restore.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, restore.ExitCode);
        }

        var validator = new DotnetBuildValidator();
        var green = await validator.ValidateAsync([source], TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(source, "public class L {\n", TestContext.Current.CancellationToken);
        var red = await validator.ValidateAsync([source], TestContext.Current.CancellationToken);

        Assert.Equal(BuildOutcome.Green, green.Outcome);
        Assert.Equal(BuildOutcome.Red, red.Outcome);
    }

    [Fact]
    [Trait("BR", "RT-41")]
    public async Task ShouldReportSkipped_WhenNoProjectAboveTheEditedFile()
    {
        var source = Path.Combine(_dir, "Lib.cs");
        await File.WriteAllTextAsync(source, "public class L { }\n", TestContext.Current.CancellationToken);

        var result = await new DotnetBuildValidator().ValidateAsync([source], TestContext.Current.CancellationToken);

        Assert.Equal(BuildOutcome.Skipped, result.Outcome);
    }
}
