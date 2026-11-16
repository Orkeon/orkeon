using System.IO.Compression;
using System.Text.Json;

namespace Orkeon.E2E.Tests;

/// <summary>
/// FWK-26: the <c>Orkeon.Templates</c> package (src/packaging/Orkeon.Templates) packs, installs into a
/// <c>dotnet new</c> hive of its own, and each of its three templates (<c>orkeon-host</c>,
/// <c>orkeon-plugin</c>, <c>orkeon-tool</c>) generates a project that compiles against the packages packed
/// from this checkout. Nothing is downloaded: the generated projects restore from a local feed of the nupkgs
/// packed at a run version of their own (<see cref="LineupPackFixture"/>, #390) and from the repository's package
/// cache (<c>RestorePackagesPath</c>), with the NuGet audit off, and the template package never reaches the machine's template engine (a throw-away <c>--debug:custom-hive</c>).
/// Category=Slow: the pack builds the lineup; the nightly integration workflow owns it.
/// </summary>
[Trait("Category", "Slow")]
public sealed class TemplatesPackTests : IClassFixture<TemplatesPackTests.TemplatesFixture>
{
    private static readonly string[] TemplateNames = ["orkeon-host", "orkeon-plugin", "orkeon-tool"];

    private readonly TemplatesFixture _fixture;

    /// <summary>Takes the shared pack, local feed and hive.</summary>
    public TemplatesPackTests(TemplatesFixture fixture) => _fixture = fixture;

    [Fact]
    public void Pack_ProducesATemplatePackage_WhoseVersionTokenIsSubstituted()
    {
        Assert.True(_fixture.PackExitCode == 0, $"Packing failed with exit code {_fixture.PackExitCode}:\n{_fixture.PackOutput}");

        var nupkg = Path.Combine(_fixture.FeedDirectory, $"Orkeon.Templates.{_fixture.Version}.nupkg");
        Assert.True(File.Exists(nupkg), $"{nupkg} was not produced.");

        using var archive = ZipFile.OpenRead(nupkg);
        var nuspec = ReadEntry(archive, e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        Assert.Contains("<packageType name=\"Template\"", nuspec, StringComparison.Ordinal);

        var projects = archive.Entries.Where(e => e.FullName.EndsWith(".csproj", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, projects.Count);
        foreach (var project in projects)
        {
            var text = ReadEntry(archive, e => e == project);
            Assert.DoesNotContain("$(OrkeonVersion)", text, StringComparison.Ordinal);
            Assert.Contains($"Version=\"{_fixture.Version}\"", text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(archive.Entries, e => e.FullName.EndsWith(".in", StringComparison.Ordinal));
        foreach (var name in TemplateNames)
            Assert.Contains(archive.Entries, e => e.FullName.EndsWith($"{name}/.template.config/template.json", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("orkeon-host")]
    [InlineData("orkeon-plugin")]
    [InlineData("orkeon-tool")]
    public void Template_GeneratesAProject_ThatBuildsOnTheLocalFeed(string shortName)
    {
        Assert.True(_fixture.InstallExitCode == 0, $"Installing the template package failed ({_fixture.InstallExitCode}):\n{_fixture.InstallOutput}");

        var project = Path.Combine(_fixture.Scratch, shortName);
        var (newExit, newOutput) = LineupPackFixture.Run("dotnet", ["new", shortName, "-o", project, "--debug:custom-hive", _fixture.Hive], _fixture.Scratch);
        Assert.True(newExit == 0, $"dotnet new {shortName} failed ({newExit}):\n{newOutput}");

        var csproj = Directory.GetFiles(project, "*.csproj").Single();
        Assert.Contains($"Version=\"{_fixture.Version}\"", File.ReadAllText(csproj), StringComparison.Ordinal);

        var (restoreExit, restoreOutput) = _fixture.Restore(csproj, project);
        Assert.True(restoreExit == 0, $"restore of {shortName} failed ({restoreExit}):\n{restoreOutput}");
        var stale = _fixture.FindStaleExtractions();
        Assert.True(stale.Count == 0, $"{shortName} restored packages this run did not pack: {string.Join(", ", stale)}.");

        var (buildExit, buildOutput) = LineupPackFixture.Run("dotnet", ["build", csproj, "--no-restore", "-warnaserror", "-nologo"], project);
        Assert.True(buildExit == 0, $"build of {shortName} failed ({buildExit}):\n{buildOutput}");
    }

    [Fact]
    public void PluginTemplate_ManifestDeclaresItsTool_AndNamesItsEntryAssembly()
    {
        Assert.True(_fixture.InstallExitCode == 0, $"Installing the template package failed ({_fixture.InstallExitCode}):\n{_fixture.InstallOutput}");

        var project = Path.Combine(_fixture.Scratch, "plugin-manifest");
        var (newExit, newOutput) = LineupPackFixture.Run("dotnet", ["new", "orkeon-plugin", "-n", "Acme.Echo", "-o", project, "--debug:custom-hive", _fixture.Hive], _fixture.Scratch);
        Assert.True(newExit == 0, $"dotnet new orkeon-plugin failed ({newExit}):\n{newOutput}");

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "plugin.json")));
        Assert.Equal("Acme.Echo.dll", manifest.RootElement.GetProperty("entryAssembly").GetString());

        var declared = manifest.RootElement.GetProperty("tools").EnumerateArray().Select(t => t.GetString()).ToList();
        var contracts = Directory.GetFiles(project, "*.cs")
            .SelectMany(f => File.ReadAllText(f).Split("[ToolContract(\"").Skip(1).Select(s => s[..s.IndexOf('"')]))
            .ToList();
        Assert.NotEmpty(contracts);
        Assert.Equal(contracts.Order(), declared.Order());
    }

    private static string ReadEntry(ZipArchive archive, Func<ZipArchiveEntry, bool> predicate)
    {
        using var reader = new StreamReader(archive.Entries.First(predicate).Open());
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Packs the lineup the templates restore from (the umbrella, the tools, the local embeddings, the CLI constants, the
    /// scripting package) and the template package into one local feed at the run version, installs the latter into a
    /// throw-away hive, and removes the scratch folder and the cache folders of the run when the class is done.
    /// </summary>
    public sealed class TemplatesFixture : LineupPackFixture
    {
        private static readonly string[] PackagedProjects =
        [
            "src/packaging/Orkeon.Templates/Orkeon.Templates.csproj",
            "src/packaging/Orkeon/Orkeon.csproj",
            "src/packaging/Orkeon.Tools/Orkeon.Tools.csproj",
            "src/packaging/Orkeon.Tools.Embeddings.Local/Orkeon.Tools.Embeddings.Local.Package.csproj",
            "src/packaging/Orkeon.Scripting/Orkeon.Scripting.Package.csproj",
            // Orkeon.Scripting depends on it as a package of its own (ADR-009).
            "src/constants/Orkeon.Constants.Cli/Orkeon.Constants.Cli.csproj",
        ];

        /// <summary>Initializes the fixture: packs, then installs; failures are kept for the tests to assert on.</summary>
        public TemplatesFixture()
            : base("orkeon-templates-", PackagedProjects)
        {
            Hive = Path.Combine(Scratch, "hive");
            Directory.CreateDirectory(Hive);
            (InstallExitCode, InstallOutput) = PackExitCode == 0
                ? Run("dotnet", ["new", "install", Path.Combine(FeedDirectory, $"Orkeon.Templates.{Version}.nupkg"), "--debug:custom-hive", Hive], Scratch)
                : (-1, "not run: the pack failed.");
        }

        /// <summary>The throw-away <c>dotnet new</c> hive.</summary>
        public string Hive { get; }

        /// <summary>Exit code of <c>dotnet new install</c>.</summary>
        public int InstallExitCode { get; }

        /// <summary>Output of <c>dotnet new install</c>.</summary>
        public string InstallOutput { get; }
    }
}
