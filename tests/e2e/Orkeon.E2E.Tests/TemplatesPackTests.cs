using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace Orkeon.E2E.Tests;

/// <summary>
/// FWK-26: the <c>Orkeon.Templates</c> package (src/packaging/Orkeon.Templates) packs, installs into a
/// <c>dotnet new</c> hive of its own, and each of its three templates (<c>orkeon-host</c>,
/// <c>orkeon-plugin</c>, <c>orkeon-tool</c>) generates a project that compiles against the packages packed
/// from this checkout. Nothing is downloaded: the generated projects restore from a local feed of the packed
/// nupkgs and from the repository's package cache (<c>RestorePackagesPath</c>), with the NuGet audit off, and
/// the template package never reaches the machine's template engine (a throw-away <c>--debug:custom-hive</c>).
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
        var (newExit, newOutput) = Run("dotnet", ["new", shortName, "-o", project, "--debug:custom-hive", _fixture.Hive], _fixture.Scratch);
        Assert.True(newExit == 0, $"dotnet new {shortName} failed ({newExit}):\n{newOutput}");

        var csproj = Directory.GetFiles(project, "*.csproj").Single();
        Assert.Contains($"Version=\"{_fixture.Version}\"", File.ReadAllText(csproj), StringComparison.Ordinal);

        var (restoreExit, restoreOutput) = Run("dotnet",
        [
            "restore", csproj, "-p:NuGetAudit=false",
            $"-p:RestoreSources={_fixture.FeedDirectory}",
            $"-p:RestorePackagesPath={_fixture.PackagesPath}",
        ], project);
        Assert.True(restoreExit == 0, $"restore of {shortName} failed ({restoreExit}):\n{restoreOutput}");

        var (buildExit, buildOutput) = Run("dotnet", ["build", csproj, "--no-restore", "-warnaserror", "-nologo"], project);
        Assert.True(buildExit == 0, $"build of {shortName} failed ({buildExit}):\n{buildOutput}");
    }

    [Fact]
    public void PluginTemplate_ManifestDeclaresItsTool_AndNamesItsEntryAssembly()
    {
        Assert.True(_fixture.InstallExitCode == 0, $"Installing the template package failed ({_fixture.InstallExitCode}):\n{_fixture.InstallOutput}");

        var project = Path.Combine(_fixture.Scratch, "plugin-manifest");
        var (newExit, newOutput) = Run("dotnet", ["new", "orkeon-plugin", "-n", "Acme.Echo", "-o", project, "--debug:custom-hive", _fixture.Hive], _fixture.Scratch);
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

    internal static (int ExitCode, string Output) Run(string fileName, IEnumerable<string> arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)TimeSpan.FromMinutes(20).TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"`{fileName}` did not exit within 20 minutes:\n{stdout.Result}\n{stderr.Result}");
        }

        return (process.ExitCode, stdout.Result + "\n" + stderr.Result);
    }

    private static string ReadEntry(ZipArchive archive, Func<ZipArchiveEntry, bool> predicate)
    {
        using var reader = new StreamReader(archive.Entries.First(predicate).Open());
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Packs the lineup the templates restore from (the umbrella, the tools, the local embeddings, the CLI constants, the
    /// scripting package) and the template package into one local feed, installs the latter into a
    /// throw-away hive, and removes the whole scratch folder when the class is done.
    /// </summary>
    public sealed class TemplatesFixture : IDisposable
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
        {
            var repoRoot = FindRepoRoot();
            PackagesPath = Path.Combine(repoRoot, "packages");
            Scratch = Path.Combine(Path.GetTempPath(), "orkeon-templates-" + Guid.NewGuid().ToString("N")[..8]);
            FeedDirectory = Path.Combine(Scratch, "feed");
            Hive = Path.Combine(Scratch, "hive");
            Directory.CreateDirectory(FeedDirectory);
            Directory.CreateDirectory(Hive);

            var props = System.Xml.Linq.XDocument.Load(Path.Combine(repoRoot, "src", "Directory.Build.props"));
            var prefix = props.Descendants("VersionPrefix").Single().Value;
            // A final version carries no VersionSuffix element at all.
            var suffix = props.Descendants("VersionSuffix").SingleOrDefault()?.Value ?? "";
            Version = suffix.Length == 0 ? prefix : $"{prefix}-{suffix}";

            var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                ? "Release" : "Debug";
            var outputs = new List<string>();
            foreach (var project in PackagedProjects)
            {
                var (exit, output) = Run("dotnet",
                    ["pack", Path.Combine(repoRoot, project), "-c", configuration, "-o", FeedDirectory, "--no-restore", "-nologo"], repoRoot);
                outputs.Add(output);
                if (exit != 0)
                {
                    PackExitCode = exit;
                    break;
                }
            }

            PackOutput = string.Join('\n', outputs);
            (InstallExitCode, InstallOutput) = PackExitCode == 0
                ? Run("dotnet", ["new", "install", Path.Combine(FeedDirectory, $"Orkeon.Templates.{Version}.nupkg"), "--debug:custom-hive", Hive], Scratch)
                : (-1, "not run: the pack failed.");
        }

        /// <summary>Folder holding the throw-away hive, the feed and the generated projects.</summary>
        public string Scratch { get; }

        /// <summary>The local feed of packed nupkgs.</summary>
        public string FeedDirectory { get; }

        /// <summary>The throw-away <c>dotnet new</c> hive.</summary>
        public string Hive { get; }

        /// <summary>The repository's package cache: every restore reads and fills it.</summary>
        public string PackagesPath { get; }

        /// <summary>The package version, from src/Directory.Build.props.</summary>
        public string Version { get; }

        /// <summary>Exit code of the first pack that failed, else 0.</summary>
        public int PackExitCode { get; }

        /// <summary>Output of the packs.</summary>
        public string PackOutput { get; }

        /// <summary>Exit code of <c>dotnet new install</c>.</summary>
        public int InstallExitCode { get; }

        /// <summary>Output of <c>dotnet new install</c>.</summary>
        public string InstallOutput { get; }

        /// <summary>Removes the scratch folder.</summary>
        public void Dispose()
        {
            try
            {
                Directory.Delete(Scratch, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The scratch folder is temporary: a leftover is not a failure.
            }
        }

        private static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "Orkeon.sln")))
                dir = Path.GetDirectoryName(dir);

            return dir ?? throw new InvalidOperationException("Orkeon.sln not found above the test base directory.");
        }
    }
}
