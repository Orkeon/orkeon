using System.Text.RegularExpressions;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// STUDIO-24: <see cref="FolderSlug"/> is the one slug implementation of the CLI and Studio.
/// Two used to coexist — the forge session's and the team catalog's — and they disagreed on
/// the cap, the cut and the fallback, so a name could not be trusted to give the folder the
/// other side would compute. This guard reads the sources of the CLI and of every Studio
/// project and fails on anything shaped like a third one: the accent-stripping idiom a slug
/// needs (a FormD decomposition, a filter on non-spacing marks), or a method from a string to
/// a string named after slugs.
/// </summary>
/// <remarks>
/// Text-level, comments stripped first, like the other source-reading guards of the
/// repository. The detector runs on the two implementations it replaced as well, so a
/// pattern that silently stopped matching fails here instead of letting a third one through.
/// </remarks>
public sealed partial class FolderSlugDriftTests
{
    /// <summary>The CLI and the Studio projects, relative to the repository root.</summary>
    private static readonly string[] ScannedProjects =
    [
        "src/scripting/Orkeon.Scripting.Cli",
        "src/apps/Orkeon.Studio.Core",
        "src/apps/Orkeon.Studio.Wpf",
        "src/apps/Orkeon.Studio.Config",
        "src/apps/Orkeon.Studio.Run",
    ];

    /// <summary>
    /// Files that fold accents for another purpose than naming a folder, each with its reason.
    /// Only the accent-stripping rule skips them — a method named after slugs is still flagged
    /// there — and <see cref="Every_exemption_still_folds_accents_and_names_no_folder"/> keeps
    /// the list from outliving the code it excuses.
    /// </summary>
    private static readonly Dictionary<string, string> AccentFoldingExemptions = new(StringComparer.Ordinal)
    {
        ["src/scripting/Orkeon.Scripting.Cli/Commands/UseCases/UseCaseText.cs"] =
            "STUDIO-38: the search normalization folds a query and a use-case sheet alike (FormKD) "
            + "so BM25 matches 'resume' with 'résumé'; it produces terms, never a folder name.",
    };

    [GeneratedRegex(@"NormalizationForm\s*\.\s*FormK?D\b|UnicodeCategory\s*\.\s*NonSpacingMark\b")]
    private static partial Regex AccentStrippingPattern();

    [GeneratedRegex(@"\bstring\??\s+\w*[Ss]lug\w*\s*\(\s*(?:this\s+)?string\??\s+\w+")]
    private static partial Regex SlugMethodPattern();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockCommentPattern();

    [GeneratedRegex(@"//[^\r\n]*")]
    private static partial Regex LineCommentPattern();

    [Fact]
    public void The_cli_and_studio_sources_carry_no_slug_implementation_of_their_own()
    {
        var root = RepositoryRoot();
        var violations = SourceFiles(root)
            .SelectMany(file =>
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                var exempt = AccentFoldingExemptions.ContainsKey(relative);
                return Findings(File.ReadAllText(file))
                    .Where(finding => !(exempt && finding.Contains("accent stripping", StringComparison.Ordinal)))
                    .Select(finding => $"{relative}:{finding}");
            })
            .ToList();

        Assert.True(
            violations.Count == 0,
            "A slug implementation outside Orkeon.Domain.FileSystem.FolderSlug (STUDIO-24): the CLI and "
            + "Studio must name a folder alike, so they share one rule. Call FolderSlug.From and apply "
            + "your own fallback (FolderSlug.TeamFallback for a team or an agent) instead:\n - "
            + string.Join("\n - ", violations));
    }

    /// <summary>
    /// A path filter that silently dropped a project, or the files the two former
    /// implementations lived in, would turn the guard above into a pass on nothing.
    /// </summary>
    [Fact]
    public void The_scan_reads_every_project_and_the_files_that_used_to_slug()
    {
        var root = RepositoryRoot();
        var files = SourceFiles(root).ToList();

        Assert.All(ScannedProjects, project =>
            Assert.Contains(files, file => Path.GetRelativePath(root, file).Replace('\\', '/').StartsWith(project + "/", StringComparison.Ordinal)));
        var names = files.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("ForgeSession.cs", names);
        Assert.Contains("TeamCatalog.cs", names);
        Assert.Contains("CreateTeamViewModel.cs", names);
        Assert.Contains("AgentEditorViewModel.cs", names);
    }

    /// <summary>
    /// An exemption outlives its reason when its file moves, or stops folding accents: then it
    /// would excuse whatever lands at that path next. It never excuses a slug method.
    /// </summary>
    [Fact]
    public void Every_exemption_still_folds_accents_and_names_no_folder()
    {
        var root = RepositoryRoot();

        Assert.All(AccentFoldingExemptions, exemption =>
        {
            var path = Path.Combine(root, exemption.Key);
            Assert.True(File.Exists(path), $"Exempted file is gone: {exemption.Key} ({exemption.Value})");
            var findings = Findings(File.ReadAllText(path));
            Assert.Contains(findings, finding => finding.Contains("accent stripping", StringComparison.Ordinal));
            Assert.DoesNotContain(findings, finding => finding.Contains("slug method", StringComparison.Ordinal));
        });
    }

    [Theory]
    [InlineData(FormerForgeSessionSlugify)]
    [InlineData(FormerTeamCatalogSlugify)]
    public void The_detector_recognises_the_two_implementations_it_replaced(string source)
    {
        var findings = Findings(source);

        Assert.Contains(findings, finding => finding.Contains("accent stripping", StringComparison.Ordinal));
        Assert.Contains(findings, finding => finding.Contains("slug method", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("public static string Slug(CaptureCategory category) => category.ToString();")]
    [InlineData("public static bool TryLoadBySlug(string workspace, string slug, out ForgeSession? session)")]
    [InlineData("var slug = FolderSlug.From(name) ?? FolderSlug.TeamFallback;")]
    [InlineData("// the former rule decomposed with NormalizationForm.FormD and dropped UnicodeCategory.NonSpacingMark")]
    [InlineData("/* private static string Slugify(string name) */")]
    public void The_detector_leaves_other_slug_vocabulary_alone(string source)
    {
        Assert.Empty(Findings(source));
    }

    /// <summary>What the guard finds in one source text, as <c>line -&gt; rule 'match'</c>.</summary>
    private static List<string> Findings(string source)
    {
        var code = StripComments(source);
        return
        [
            .. AccentStrippingPattern().Matches(code).Select(match => Describe(code, match, "accent stripping")),
            .. SlugMethodPattern().Matches(code).Select(match => Describe(code, match, "slug method")),
        ];
    }

    private static string Describe(string code, Match match, string rule) =>
        $"{1 + code.AsSpan(0, match.Index).Count('\n')} -> {rule} '{match.Value}'";

    /// <summary>Block and line comments removed, line numbers kept.</summary>
    private static string StripComments(string source)
    {
        var withoutBlocks = BlockCommentPattern().Replace(source, match => new string('\n', match.Value.Count(c => c == '\n')));
        return LineCommentPattern().Replace(withoutBlocks, string.Empty);
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        ScannedProjects
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, project), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj" or "obj-linux"))
            .Order(StringComparer.Ordinal);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Orkeon.sln")))
            directory = directory.Parent;

        Assert.True(directory is not null, $"Could not locate the repo root above {AppContext.BaseDirectory}.");
        return directory!.FullName;
    }

    /// <summary>The CLI's former session slugifier, as it stood before STUDIO-24.</summary>
    private const string FormerForgeSessionSlugify = """
        private static string? Slugify(string? requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return null;

            var builder = new StringBuilder(requested.Length);
            var normalized = new string(
                [.. requested.Trim().Normalize(NormalizationForm.FormD)
                    .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)])
                .ToLowerInvariant();
            return builder.ToString().TrimEnd('-');
        }
        """;

    /// <summary>Studio's former team slugifier, as it stood before STUDIO-24.</summary>
    private const string FormerTeamCatalogSlugify = """
        public static string Slugify(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            var normalized = name.Normalize(System.Text.NormalizationForm.FormD);
            foreach (var ch in normalized)
            {
                var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category == System.Globalization.UnicodeCategory.NonSpacingMark)
                    continue;
            }

            return "equipe";
        }
        """;
}
