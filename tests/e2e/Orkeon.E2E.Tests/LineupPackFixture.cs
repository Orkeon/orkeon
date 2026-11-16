using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Orkeon.E2E.Tests;

/// <summary>
/// Packs part of the Orkeon lineup into a local feed under a version of its own, so a consumer restored into the
/// repository's package cache (<c>RestorePackagesPath</c>, <c>&lt;repo&gt;/packages</c>) always gets the nupkgs this run
/// packed (#390). NuGet takes a package from that cache by id and version before it reads a source: packed at the
/// product version of <c>src/Directory.Build.props</c>, a consumer restored the copy an earlier commit had extracted
/// there, and a change to a packaged <c>.targets</c> or to the package content went unseen until the next version bump.
/// The run version is the product version with the prerelease identifier <c>e2e-&lt;8 hex&gt;</c> appended; dispose
/// removes the <c>orkeon*/&lt;run version&gt;</c> folders the restores extracted, and start-up removes those a killed
/// run left behind once they are old enough; nothing else of the cache is touched: third-party packages stay shared
/// and are never downloaded again. Every restore source is the local feed alone. The nuspecs and the staged templates
/// of the packs go to the scratch folder, not to the checkout's <c>obj</c>.
/// </summary>
public abstract class LineupPackFixture : IDisposable
{
    // A run killed before its dispose leaves its folders behind: the next run removes those older than this.
    private static readonly TimeSpan LeftoverAge = TimeSpan.FromHours(12);

    private static readonly Regex RunVersionFolder = new(@"(^|[.-])e2e-[0-9a-f]{8}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Packs <paramref name="packagedProjects"/> (paths from the repository root) in order into the feed.</summary>
    protected LineupPackFixture(string scratchPrefix, IReadOnlyList<string> packagedProjects)
    {
        RepoRoot = FindRepoRoot();
        PackagesPath = Path.Combine(RepoRoot, "packages");
        var runId = Guid.NewGuid().ToString("N")[..8];
        Scratch = Path.Combine(Path.GetTempPath(), scratchPrefix + runId);
        FeedDirectory = Path.Combine(Scratch, "feed");
        Directory.CreateDirectory(FeedDirectory);

        var props = System.Xml.Linq.XDocument.Load(Path.Combine(RepoRoot, "src", "Directory.Build.props"));
        var prefix = props.Descendants("VersionPrefix").Single().Value;
        // A final version carries no VersionSuffix element at all.
        var suffix = props.Descendants("VersionSuffix").SingleOrDefault()?.Value ?? "";
        // One alphanumeric identifier: a hex run id made of digits alone may start with 0, which SemVer refuses
        // in a numeric identifier.
        Version = suffix.Length == 0 ? $"{prefix}-e2e-{runId}" : $"{prefix}-{suffix}.e2e-{runId}";

        RemoveLeftovers();

        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            ? "Release" : "Debug";
        var outputs = new List<string>();
        foreach (var project in packagedProjects)
        {
            // PackageVersion, not Version: the assemblies are not rebuilt, and the global property reaches every project
            // reference, so the nuspec dependencies between lineup packages carry the run version too. The nuspec of a
            // run and the staged templates (which hold the run version) stay in the scratch folder: a concurrent pass
            // writes its own.
            var (exit, output) = Run("dotnet",
                [
                    "pack", Path.Combine(RepoRoot, project), "-c", configuration, "-o", FeedDirectory, "--no-restore", "-nologo",
                    $"-p:PackageVersion={Version}",
                    $"-p:NuspecOutputPath={Path.Combine(Scratch, "nuspec")}{Path.DirectorySeparatorChar}",
                    $"-p:OrkeonTemplateStageDirectory={Path.Combine(Scratch, "template-stage")}{Path.DirectorySeparatorChar}",
                ],
                RepoRoot);
            outputs.Add(output);
            if (exit != 0)
            {
                PackExitCode = exit;
                break;
            }
        }

        PackOutput = string.Join('\n', outputs);
    }

    /// <summary>The checkout holding <c>Orkeon.sln</c>.</summary>
    public string RepoRoot { get; }

    /// <summary>Folder holding the feed and the consumers.</summary>
    public string Scratch { get; }

    /// <summary>The local feed of packed nupkgs.</summary>
    public string FeedDirectory { get; }

    /// <summary>The repository's package cache: every restore reads and fills it.</summary>
    public string PackagesPath { get; }

    /// <summary>
    /// The version every package of this run is packed at: the product version of src/Directory.Build.props followed by
    /// the run identifier <c>e2e-&lt;8 hex&gt;</c>.
    /// </summary>
    public string Version { get; }

    /// <summary>Exit code of the first pack that failed, else 0.</summary>
    public int PackExitCode { get; }

    /// <summary>Output of the packs.</summary>
    public string PackOutput { get; }

    /// <summary>Restores <paramref name="csproj"/> from the local feed into the repository's package cache, audit off.</summary>
    public (int ExitCode, string Output) Restore(string csproj, string workingDirectory) =>
        Run("dotnet",
        [
            "restore", csproj, "-p:NuGetAudit=false",
            $"-p:RestoreSources={FeedDirectory}",
            $"-p:RestorePackagesPath={PackagesPath}",
        ], workingDirectory);

    /// <summary>
    /// The Orkeon packages the restore of the project in <paramref name="projectDirectory"/> resolved that are not the
    /// nupkgs this run packed: an Orkeon library of its <c>project.assets.json</c> at another version than
    /// <see cref="Version"/> (a copy an earlier run extracted into the cache), absent from the feed, or whose SHA-512
    /// differs from the nupkg of the feed. Empty when every Orkeon package came from this run; a project with no assets
    /// file yields one entry saying so.
    /// </summary>
    public IReadOnlyList<string> FindPackagesNotFromThisRun(string projectDirectory)
    {
        var assets = Directory.GetFiles(projectDirectory, "project.assets.json", SearchOption.AllDirectories).FirstOrDefault();
        if (assets is null)
            return [$"no project.assets.json under {projectDirectory}"];

        using var document = JsonDocument.Parse(File.ReadAllText(assets));
        var foreign = new List<string>();
        foreach (var library in document.RootElement.GetProperty("libraries").EnumerateObject())
        {
            var slash = library.Name.IndexOf('/', StringComparison.Ordinal);
            var id = library.Name[..slash];
            var version = library.Name[(slash + 1)..];
            if (!id.StartsWith("Orkeon", StringComparison.OrdinalIgnoreCase)
                || !library.Value.TryGetProperty("type", out var type) || type.GetString() != "package")
                continue;

            var nupkg = Path.Combine(FeedDirectory, $"{id}.{Version}.nupkg");
            if (!string.Equals(version, Version, StringComparison.OrdinalIgnoreCase))
            {
                foreign.Add($"{library.Name} (not the run version {Version})");
                continue;
            }

            if (!File.Exists(nupkg))
            {
                foreign.Add($"{library.Name} (not packed by this run)");
                continue;
            }

            using var stream = File.OpenRead(nupkg);
            var packed = Convert.ToBase64String(SHA512.HashData(stream));
            var restored = library.Value.TryGetProperty("sha512", out var sha) ? sha.GetString() : null;
            if (restored != packed)
                foreign.Add($"{library.Name} (SHA-512 differs from the nupkg of the feed)");
        }

        return foreign;
    }

    /// <summary>
    /// Removes the cache folders this run's restores extracted, then the scratch folder. An id folder is kept, even
    /// empty: a concurrent pass may be extracting into it.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases what the fixture created.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        if (Directory.Exists(PackagesPath))
        {
            foreach (var idFolder in Directory.GetDirectories(PackagesPath, "orkeon*"))
                TryDelete(Path.Combine(idFolder, Version.ToLowerInvariant()));
        }

        TryDelete(Scratch);
    }

    /// <summary>Runs a process to its end (20 minutes at most) and returns its exit code and its two streams.</summary>
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

    // Only run versions are touched (the e2e-<8 hex> identifier no published version carries), and only once they are
    // old enough that no concurrent pass still restores from them.
    private void RemoveLeftovers()
    {
        if (!Directory.Exists(PackagesPath))
            return;

        var before = DateTime.UtcNow - LeftoverAge;
        foreach (var idFolder in Directory.GetDirectories(PackagesPath, "orkeon*"))
        {
            foreach (var versionFolder in Directory.GetDirectories(idFolder))
            {
                if (RunVersionFolder.IsMatch(Path.GetFileName(versionFolder)) && Directory.GetLastWriteTimeUtc(versionFolder) < before)
                    TryDelete(versionFolder);
            }
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover is not a failure: the next run removes a run version once it is old enough.
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
