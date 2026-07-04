using System.Text.RegularExpressions;

namespace Orkeon.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// Architecture guard (R10.3 — ORG-012): DI registration files in <c>src/</c> must stay pure
/// synchronous wiring. Service factories run under the container's singleton-resolution lock;
/// blocking there on async work (<c>GetAwaiter().GetResult()</c>, <c>.Result</c>) stacks up
/// concurrent resolutions — the historical example being the Docker availability probe
/// (a <c>docker version</c> child process, up to 10 s) in the <c>ICodeSandbox</c> factory.
/// Move the async work into the component instead (lazy + memoized — see
/// <c>LazyProbingCodeSandbox</c> or the DPAPI lazy-init precedent of ex-ORG-005).
/// </summary>
/// <remarks>
/// Pragmatic implementation: scans the sources of every <c>src/**/DependencyInjection/*.cs</c>
/// file (direct file reads are allowed in tests). DI extension files are registration code by
/// construction, so the forbidden motifs are flagged anywhere in the file (comments stripped
/// first). The repository root is located by walking up from <see cref="AppContext.BaseDirectory"/>
/// to the <c>Orkeon.sln</c> marker.
/// </remarks>
public partial class DiRegistrationSyncOverAsyncPolicyTests
{
    /// <summary>
    /// Forbidden blocking motifs: <c>GetAwaiter().GetResult()</c> (whitespace/newline tolerant)
    /// and <c>.Result</c> (word-bounded, so <c>.GetResult</c>/<c>.ResultType</c> do not match).
    /// </summary>
    [GeneratedRegex(@"GetAwaiter\s*\(\s*\)\s*\.\s*GetResult\s*\(\s*\)|\.\s*Result\b")]
    private static partial Regex SyncOverAsyncPattern();

    /// <summary>
    /// Nominative exemptions (file name → reason). Keep this list as small as possible;
    /// every entry must point at an existing scanned file (stale entries fail the test).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ExemptedFiles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Plugin discovery is deliberately eager and blocking at REGISTRATION time
            // (plugins contribute registrations to the IServiceCollection being built,
            // before any provider — and any resolution lock — exists). Documented as
            // out of scope for R10.3/ORG-012.
            ["OrkeonPluginsServiceCollectionExtensions.cs"] =
                "Eager plugin discovery at registration time, before the service provider " +
                "is built — documented out of scope for R10.3/ORG-012.",
        };

    [Fact]
    public void ShouldNotBlockOnAsyncResults_InSrcDependencyInjectionFiles()
    {
        var repoRoot = FindRepositoryRoot();
        var diFiles = FindDependencyInjectionSourceFiles(repoRoot);

        // Sanity: the scan must actually cover the DI surface (a broken path filter
        // would otherwise make this guard pass vacuously).
        Assert.NotEmpty(diFiles);
        Assert.Contains(diFiles, f =>
            Path.GetFileName(f) == "InfrastructureExtensions.cs");

        var violations = new List<string>();
        foreach (var file in diFiles)
        {
            if (ExemptedFiles.ContainsKey(Path.GetFileName(file)))
                continue;

            var source = StripComments(File.ReadAllText(file));
            foreach (Match match in SyncOverAsyncPattern().Matches(source))
            {
                var line = 1 + CountNewlines(source, match.Index);
                var relativePath = Path.GetRelativePath(repoRoot, file);
                violations.Add($"{relativePath}:{line} -> '{Collapse(match.Value)}'");
            }
        }

        if (violations.Count > 0)
        {
            Assert.Fail(
                "Sync-over-async blocking found in src/ DI registration files (R10.3 — ORG-012). " +
                "Service factories run under the container's singleton-resolution lock; move the " +
                "async work into the component (lazy, memoized — see LazyProbingCodeSandbox) " +
                "instead of blocking the wiring:\n - " + string.Join("\n - ", violations));
        }
    }

    [Fact]
    public void ShouldKeepExemptionListCurrent_WhenScanningDependencyInjectionFiles()
    {
        var repoRoot = FindRepositoryRoot();
        var diFileNames = FindDependencyInjectionSourceFiles(repoRoot)
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);

        var staleExemptions = ExemptedFiles.Keys
            .Where(name => !diFileNames.Contains(name))
            .ToList();

        if (staleExemptions.Count > 0)
        {
            Assert.Fail(
                "Stale exemption(s) in DiRegistrationSyncOverAsyncPolicyTests — the file no " +
                "longer exists under src/**/DependencyInjection/; remove the entry: " +
                string.Join(", ", staleExemptions));
        }
    }

    // --- Helpers ---

    /// <summary>
    /// Walks up from <see cref="AppContext.BaseDirectory"/> to the directory containing the
    /// <c>Orkeon.sln</c> marker (same approach as other repo-reading tests, made hop-count
    /// independent).
    /// </summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Orkeon.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root (Orkeon.sln marker) above '{AppContext.BaseDirectory}'.");
    }

    private static List<string> FindDependencyInjectionSourceFiles(string repoRoot)
    {
        var srcRoot = Path.Combine(repoRoot, "src");
        Assert.True(Directory.Exists(srcRoot), $"src/ not found under repository root '{repoRoot}'.");

        return Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(IsDependencyInjectionSourceFile)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsDependencyInjectionSourceFile(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Contains("bin") || segments.Contains("obj") || segments.Contains("obj-linux"))
            return false;
        return segments.Contains("DependencyInjection");
    }

    /// <summary>
    /// Removes block and line comments while preserving line numbers. Tripwire-grade only:
    /// a <c>//</c> embedded in a string literal (e.g. a URL) also truncates that line, which
    /// can under-match but never produces a false positive.
    /// </summary>
    private static string StripComments(string source)
    {
        var withoutBlocks = BlockCommentRegex().Replace(
            source,
            m => new string('\n', m.Value.Count(c => c == '\n')));
        return LineCommentRegex().Replace(withoutBlocks, string.Empty);
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockCommentRegex();

    [GeneratedRegex(@"//[^\r\n]*")]
    private static partial Regex LineCommentRegex();

    private static int CountNewlines(string text, int endExclusive)
    {
        var count = 0;
        for (var i = 0; i < endExclusive; i++)
        {
            if (text[i] == '\n')
                count++;
        }

        return count;
    }

    private static string Collapse(string value) =>
        WhitespaceRegex().Replace(value, string.Empty);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
