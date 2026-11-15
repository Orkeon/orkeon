namespace Orkeon.E2E.Tests;

/// <summary>
/// CH-01 / CH-02: a consumer of the packed <c>Orkeon.Scripting</c> that sets <c>ExcludeAssets="build"</c> builds
/// without the local embedding model download (no <c>LocalEmbeddingsModel/</c> in its output); a consumer that
/// keeps the default reference runs the upstream download target, which fails here because no network is reachable.
/// Nothing is downloaded: restore reads a local feed and the repository's package cache.
/// Category=Slow: the pack builds the lineup.
/// </summary>
[Trait("Category", "Slow")]
public sealed class LocalEmbeddingsModelOptOutTests : IClassFixture<LocalEmbeddingsModelOptOutTests.PackFixture>
{
    private readonly PackFixture _fixture;

    /// <summary>Takes the shared pack and local feed.</summary>
    public LocalEmbeddingsModelOptOutTests(PackFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("BR", "CH-01")]
    public void ShouldBuildWithoutTheModel_WhenTheConsumerExcludesBuildAssets()
    {
        var result = BuildConsumer("optout", excludeBuild: true);

        Assert.True(result.BuildExit == 0, $"The opted-out consumer failed to build ({result.BuildExit}):\n{result.BuildOutput}");
        // The upstream targets copy the model to <OutDir>/LocalEmbeddingsModel/ (TargetPath), OutDir being bin/Release/net10.0:
        // the whole bin folder is searched, so the check holds whatever the target framework folder is.
        Assert.True(Directory.Exists(result.OutputDirectory), $"{result.OutputDirectory} was not produced by the build.");
        var found = Directory.GetDirectories(result.OutputDirectory, "LocalEmbeddingsModel", SearchOption.AllDirectories);
        Assert.True(found.Length == 0, $"LocalEmbeddingsModel/ was found: {string.Join(", ", found)}.");
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public void ShouldRunTheModelDownload_WhenTheConsumerKeepsBuildAssets()
    {
        var result = BuildConsumer("default", excludeBuild: false);

        Assert.True(result.BuildExit != 0, $"The default consumer was expected to fail offline, but built:\n{result.BuildOutput}");
        Assert.True(
            result.BuildOutput.Contains("SmartComponents.LocalEmbeddings.targets", StringComparison.Ordinal)
            && result.BuildOutput.Contains("MSB3923", StringComparison.Ordinal),
            $"The upstream model download (MSB3923 in SmartComponents.LocalEmbeddings.targets) did not run:\n{result.BuildOutput}");
    }

    private (int BuildExit, string BuildOutput, string OutputDirectory) BuildConsumer(string name, bool excludeBuild)
    {
        Assert.True(_fixture.PackExitCode == 0, $"Packing failed with exit code {_fixture.PackExitCode}:\n{_fixture.PackOutput}");

        var project = Path.Combine(_fixture.Scratch, name);
        var cache = Path.Combine(_fixture.Scratch, name + "-model-cache");
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(cache);

        var exclude = excludeBuild ? " ExcludeAssets=\"build\"" : "";
        var csproj = Path.Combine(project, "Consumer.csproj");
        File.WriteAllText(csproj, $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Orkeon.Scripting" Version="{{_fixture.Version}}"{{exclude}} />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(project, "Program.cs"), "return 0;\n");

        var (restoreExit, restoreOutput) = TemplatesPackTests.Run("dotnet",
        [
            "restore", csproj, "-p:NuGetAudit=false",
            $"-p:RestoreSources={_fixture.FeedDirectory}",
            $"-p:RestorePackagesPath={_fixture.PackagesPath}",
        ], project);
        Assert.True(restoreExit == 0, $"restore of {name} failed ({restoreExit}):\n{restoreOutput}");

        var (buildExit, buildOutput) = TemplatesPackTests.Run("dotnet",
        [
            "build", csproj, "--no-restore", "-nologo", "-c", "Release", "-v:n",
            "-p:LocalEmbeddingsModelUrl=http://127.0.0.1:9/none",
            "-p:LocalEmbeddingsVocabUrl=http://127.0.0.1:9/none",
            $"-p:LocalEmbeddingsModelCacheDir={cache}{Path.DirectorySeparatorChar}",
        ], project);
        return (buildExit, buildOutput, Path.Combine(project, "bin"));
    }

    /// <summary>Packs the lineup the consumer restores from into one local feed; removes the scratch folder at the end.</summary>
    public sealed class PackFixture : IDisposable
    {
        private static readonly string[] PackagedProjects =
        [
            "src/packaging/Orkeon/Orkeon.csproj",
            "src/packaging/Orkeon.Tools/Orkeon.Tools.csproj",
            "src/packaging/Orkeon.Tools.Embeddings.Local/Orkeon.Tools.Embeddings.Local.Package.csproj",
            "src/packaging/Orkeon.Scripting/Orkeon.Scripting.Package.csproj",
            "src/constants/Orkeon.Constants.Cli/Orkeon.Constants.Cli.csproj",
        ];

        /// <summary>Initializes the fixture: packs; failures are kept for the tests to assert on.</summary>
        public PackFixture()
        {
            var repoRoot = FindRepoRoot();
            PackagesPath = Path.Combine(repoRoot, "packages");
            Scratch = Path.Combine(Path.GetTempPath(), "orkeon-optout-" + Guid.NewGuid().ToString("N")[..8]);
            FeedDirectory = Path.Combine(Scratch, "feed");
            Directory.CreateDirectory(FeedDirectory);

            var props = System.Xml.Linq.XDocument.Load(Path.Combine(repoRoot, "src", "Directory.Build.props"));
            var prefix = props.Descendants("VersionPrefix").Single().Value;
            var suffix = props.Descendants("VersionSuffix").SingleOrDefault()?.Value ?? "";
            Version = suffix.Length == 0 ? prefix : $"{prefix}-{suffix}";

            var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                ? "Release" : "Debug";
            var outputs = new List<string>();
            foreach (var project in PackagedProjects)
            {
                var (exit, output) = TemplatesPackTests.Run("dotnet",
                    ["pack", Path.Combine(repoRoot, project), "-c", configuration, "-o", FeedDirectory, "--no-restore", "-nologo"], repoRoot);
                outputs.Add(output);
                if (exit != 0)
                {
                    PackExitCode = exit;
                    break;
                }
            }

            PackOutput = string.Join('\n', outputs);
        }

        /// <summary>Folder holding the feed and the consumers.</summary>
        public string Scratch { get; }

        /// <summary>The local feed of packed nupkgs.</summary>
        public string FeedDirectory { get; }

        /// <summary>The repository's package cache.</summary>
        public string PackagesPath { get; }

        /// <summary>The package version, from src/Directory.Build.props.</summary>
        public string Version { get; }

        /// <summary>Exit code of the first pack that failed, else 0.</summary>
        public int PackExitCode { get; }

        /// <summary>Output of the packs.</summary>
        public string PackOutput { get; }

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
