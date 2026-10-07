using System.Globalization;
using System.Text.RegularExpressions;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// A string of the localization port is formatted in one place,
/// <see cref="StudioStringsFormatting"/>: a call site names the key and hands its arguments
/// over, it never passes the pattern it read to <c>string.Format</c>. A static analysis that
/// follows the data flow (CodeQL's <c>cs/invalid-string-formatting</c>) sees every string of
/// the table reach every such call, and reports each one whose arguments are fewer than the
/// placeholders of the longest pattern. This guard reads the sources of Core and of the WPF
/// front and fails on a <c>string.Format</c> whose pattern is a direct read of the table.
/// </summary>
/// <remarks>
/// Text-level, comments stripped first, like the other source-reading guards of the
/// repository. A pattern that went through a local variable is out of its reach.
/// </remarks>
public sealed partial class StudioStringsFormattingTests
{
    /// <summary>The projects whose strings come from the port, relative to the repository root.</summary>
    private static readonly string[] ScannedProjects =
    [
        "src/apps/Orkeon.Studio.Core",
        "src/apps/Orkeon.Studio.Wpf",
    ];

    /// <summary>The file of the one formatting point, where the one direct call lives.</summary>
    private const string FormattingPoint = "src/apps/Orkeon.Studio.Core/Localization/StudioStrings.cs";

    [GeneratedRegex(
        @"\b[Ss]tring\s*\.\s*Format\s*\(\s*(?:[^,;(){}]+(?:\([^()]*\))?\s*,\s*)?"
        + @"(?:(?:[\w.]*[Ss]trings(?:\s*\.\s*Instance)?|I18n\s*\.\s*Instance)\s*\[|I18n\s*\.\s*T\s*\()")]
    private static partial Regex DirectFormatPattern();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockCommentPattern();

    [GeneratedRegex(@"//[^\r\n]*")]
    private static partial Regex LineCommentPattern();

    /// <summary>Hand-written French port, the shape a front-end bridge produces.</summary>
    private sealed class FrenchStrings : IStudioStrings
    {
        public string this[string key] => key switch
        {
            StudioStringKeys.UsageCache => "cache {0} % · {1} jetons",
            _ => key,
        };

        public event EventHandler? CultureChanged { add { } remove { } }
    }

    [Fact]
    public void The_arguments_are_formatted_in_the_culture_asked_for()
    {
        var strings = new FrenchStrings();

        var french = strings.Format(CultureInfo.GetCultureInfo("fr-FR"), StudioStringKeys.UsageCache, 12.5, "1 024");
        var invariant = strings.Format(CultureInfo.InvariantCulture, StudioStringKeys.UsageCache, 12.5, "1 024");

        Assert.Equal("cache 12,5 % · 1 024 jetons", french);
        Assert.Equal("cache 12.5 % · 1 024 jetons", invariant);
    }

    [Fact]
    public void Without_a_culture_the_arguments_follow_the_current_one()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            var line = new FrenchStrings().Format(StudioStringKeys.UsageCache, 12.5, "1 024");

            Assert.Equal("cache 12,5 % · 1 024 jetons", line);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void An_unknown_key_is_rendered_as_itself_like_the_indexer()
    {
        var strings = EnglishStudioStrings.Instance;

        Assert.Equal(strings["Studio.Not.A.Key"], strings.Format("Studio.Not.A.Key", 3));
        Assert.Equal("Studio.Not.A.Key", strings.Format(CultureInfo.InvariantCulture, "Studio.Not.A.Key", 3));
    }

    [Fact]
    public void No_source_hands_string_Format_a_pattern_it_read_from_the_table()
    {
        var root = RepositoryRoot();
        var violations = SourceFiles(root)
            .Select(file => (File: file, Relative: Path.GetRelativePath(root, file).Replace('\\', '/')))
            .Where(source => source.Relative != FormattingPoint)
            .SelectMany(source => Findings(File.ReadAllText(source.File)).Select(finding => $"{source.Relative}:{finding}"))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "A string.Format whose pattern is read from the string table: every string of the table can reach it, "
            + "so a data-flow analysis counts the placeholders of all of them against its arguments. Name the key "
            + "instead -- strings.Format(key, arguments), or strings.Format(culture, key, arguments) when the "
            + "culture is not the current one (StudioStringsFormatting):\n - "
            + string.Join("\n - ", violations));
    }

    /// <summary>
    /// The exempted file holds the formatting point and nothing else of that shape: a second
    /// direct call there would hide behind the exemption.
    /// </summary>
    [Fact]
    public void The_formatting_point_is_the_only_direct_call_of_its_file()
    {
        var findings = Findings(File.ReadAllText(Path.Combine(RepositoryRoot(), FormattingPoint)));

        Assert.Single(findings);
    }

    /// <summary>A path filter that silently dropped a project would turn the guard into a pass on nothing.</summary>
    [Fact]
    public void The_scan_reads_both_projects()
    {
        var root = RepositoryRoot();
        var files = SourceFiles(root).ToList();

        Assert.All(ScannedProjects, project =>
            Assert.Contains(files, file => Path.GetRelativePath(root, file).Replace('\\', '/').StartsWith(project + "/", StringComparison.Ordinal)));
        var names = files.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("LaunchOutcomeFormatter.cs", names);
        Assert.Contains("TeamsViewModel.cs", names);
        Assert.DoesNotContain(files, file => file.EndsWith(".g.cs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("var line = string.Format(CultureInfo.CurrentCulture, _strings[StudioStringKeys.TeamsDaily], time);")]
    [InlineData("var line = string.Format(CultureInfo.InvariantCulture, strings[key], path);")]
    [InlineData("var line = string.Format(culture, Strings[key], count);")]
    [InlineData("var line = String.Format(_strings[key], count);")]
    [InlineData("var line = string.Format(\n    CultureInfo.CurrentCulture,\n    _strings[StudioStringKeys.TeamsDaily],\n    time);")]
    [InlineData("var line = string.Format(CultureInfo.GetCultureInfo(\"fr-FR\"), _strings[key], count);")]
    [InlineData("var line = string.Format(CultureInfo.CurrentCulture, I18nStudioStrings.Instance[key], folder);")]
    [InlineData("var line = string.Format(CultureInfo.CurrentCulture, I18n.Instance[key], folder);")]
    [InlineData("var line = string.Format(CultureInfo.CurrentCulture, I18n.T(\"Studio.Shell.CounterPattern\"), index, count);")]
    public void The_detector_recognises_a_pattern_read_from_the_table(string source)
    {
        Assert.Single(Findings(source));
    }

    [Theory]
    [InlineData("var line = _strings.Format(StudioStringKeys.TeamsDaily, time);")]
    [InlineData("var line = strings.Format(CultureInfo.InvariantCulture, key, path);")]
    [InlineData("var line = string.Format(CultureInfo.InvariantCulture, \"{0}:{1}\", host, port);")]
    [InlineData("var line = string.Format(CultureInfo.CurrentCulture, \"{0} {1}\", _strings[key], count);")]
    [InlineData("var line = string.Format(culture, template, args);")]
    [InlineData("// string.Format(CultureInfo.CurrentCulture, _strings[key], name) is the form this replaces")]
    [InlineData("/* string.Format(culture, strings[key], name) */")]
    public void The_detector_leaves_other_formatting_alone(string source)
    {
        Assert.Empty(Findings(source));
    }

    /// <summary>What the guard finds in one source text, as <c>line -&gt; 'match'</c>.</summary>
    private static List<string> Findings(string source)
    {
        var code = StripComments(source);
        return
        [
            .. DirectFormatPattern().Matches(code)
                .Select(match => $"{1 + code.AsSpan(0, match.Index).Count('\n')} -> '{match.Value}'"),
        ];
    }

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
}
