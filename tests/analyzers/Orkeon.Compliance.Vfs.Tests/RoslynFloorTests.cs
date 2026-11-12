using System.Reflection;

namespace Orkeon.Compliance.Vfs.Tests;

/// <summary>
/// Guards the Roslyn floor of the shipped analyzer. A compiler refuses (CS9057, a warning, then
/// silence) an analyzer that references a <c>Microsoft.CodeAnalysis</c> newer than itself, so the
/// package is inert under any SDK whose compiler is older than the reference. The csproj pins
/// <c>VersionOverride="4.8.0"</c> (the .NET 8.0.100 compiler); repository-wide Roslyn bumps raised
/// it to the repository's own compiler twice, and the in-repo build never noticed, since it runs
/// that newer compiler. This test reads the reference the compiled assembly actually carries.
/// </summary>
public sealed class RoslynFloorTests
{
    private static readonly Version s_floor = new(4, 8, 0, 0);

    [Theory]
    [InlineData("Microsoft.CodeAnalysis")]
    [InlineData("Microsoft.CodeAnalysis.CSharp")]
    public void The_Analyzer_References_Roslyn_No_Newer_Than_The_Floor(string assemblyName)
    {
        AssemblyName? reference = typeof(SystemIoUsageAnalyzer).Assembly
            .GetReferencedAssemblies()
            .SingleOrDefault(a => string.Equals(a.Name, assemblyName, StringComparison.Ordinal));

        Assert.NotNull(reference);
        Assert.NotNull(reference.Version);
        Assert.True(
            reference.Version <= s_floor,
            $"Orkeon.Compliance.Vfs references {assemblyName} {reference.Version}, above {s_floor}: "
            + "a stock SDK with an older compiler skips the analyzer (CS9057). Keep "
            + "VersionOverride=\"4.8.0\" on Microsoft.CodeAnalysis.CSharp in Orkeon.Compliance.Vfs.csproj.");
    }
}
