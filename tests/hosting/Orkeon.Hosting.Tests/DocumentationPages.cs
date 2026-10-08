using System.Text.RegularExpressions;
using Orkeon.Tests.Shared.Produced;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The pages a reader learns the settings from, by their path from the repository's root — the
/// documentation in both languages, the READMEs and the contribution guides, the READMEs of the
/// examples, the README of an archive —, and what a guard of the settings reads in them: their
/// <c>json</c> blocks, and their prose without the code and without the tables the catalogue produces.
/// </summary>
internal static partial class DocumentationPages
{
    /// <summary>
    /// The mark that takes a <c>json</c> block out of the settings guard: alone on the line above the
    /// fence, for a block that shows on purpose what a host refuses.
    /// </summary>
    public const string Exclusion = "<!-- settings-check:off -->";

    private static readonly string[] s_folders =
    [
        "docs", "examples", "scripts/installer-assets",
        "README.md", "README.fr.md", "CONTRIBUTING.md", "CONTRIBUTING.fr.md",
    ];

    /// <summary>Every page, sorted.</summary>
    public static IReadOnlyList<string> All() =>
        RepositoryFiles.Under(
            s_folders,
            path => path.EndsWith(".md", StringComparison.Ordinal) || path.EndsWith(".md.tmpl", StringComparison.Ordinal));

    /// <summary>The text of <paramref name="page"/>, line feeds only.</summary>
    public static string Text(string page) =>
        ProducedFile.Read(page) ?? throw new InvalidOperationException($"{page} is listed and cannot be read.");

    /// <summary>The fenced blocks of <paramref name="markdown"/> a renderer colours as JSON.</summary>
    public static IEnumerable<DocumentationBlock> JsonBlocks(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        foreach (Match fence in Fence().Matches(markdown))
        {
            var language = fence.Groups["info"].Value.Trim();
            if (!language.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                continue;

            var above = markdown[..fence.Index].TrimEnd();
            yield return new DocumentationBlock(
                Line: 1 + markdown.AsSpan(0, fence.Groups["body"].Index).Count('\n'),
                Text: fence.Groups["body"].Value,
                Excluded: above.EndsWith(Exclusion, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// <paramref name="markdown"/> with every fenced block and every table the settings catalogue
    /// produces blanked, line for line: what is left is written by hand, and a line keeps its number.
    /// </summary>
    public static string HandWritten(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        static string Blank(Match match) => new('\n', match.Value.AsSpan().Count('\n'));
        return Produced().Replace(Fence().Replace(markdown, Blank), Blank);
    }

    [GeneratedRegex(@"^[ \t]*(?<fence>```+|~~~+)(?<info>[^\n`]*)\n(?<body>.*?)^[ \t]*\k<fence>[ \t]*$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex Fence();

    [GeneratedRegex(@"<!-- settings(?::[^\s]+|-index) -->.*?<!-- /settings(?:-index)? -->", RegexOptions.Singleline)]
    private static partial Regex Produced();
}

/// <summary>One fenced block of a page.</summary>
/// <param name="Line">The line its content starts on, from 1.</param>
/// <param name="Text">Its content.</param>
/// <param name="Excluded">Whether the line above the fence is <see cref="DocumentationPages.Exclusion"/>.</param>
internal sealed record DocumentationBlock(int Line, string Text, bool Excluded);
