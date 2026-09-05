using System.Text;
using System.Text.RegularExpressions;
using Orkeon.Domain.FileSystem;
using Orkeon.Rag.Abstractions.Models;

namespace Orkeon.Rag.Abstractions;

/// <summary>
/// Expands glob patterns (<c>*</c>, <c>**</c>, <c>?</c>) in source locations into
/// concrete per-file <see cref="SourceDescriptor"/>s, walking the virtual file
/// system (<see cref="IFileSystemService"/> — never <c>System.IO</c> directly).
/// Shared by every ingestion surface (agent tool <c>rag_ingest</c>, CLI
/// <c>orkeon rag ingest</c>, scripting <c>rag.ingest</c>), so glob semantics stay
/// identical across them. Locations without wildcards pass through untouched —
/// URLs, inline references, and plain file paths are the loaders' business.
/// </summary>
public static class SourceGlobExpander
{
    private static readonly char[] WildcardChars = ['*', '?'];

    /// <summary>True when <paramref name="location"/> contains a glob wildcard.</summary>
    public static bool HasWildcard(string location)
        => !string.IsNullOrEmpty(location) && location.IndexOfAny(WildcardChars) >= 0;

    /// <summary>
    /// Expands each location into one or more <see cref="SourceDescriptor"/>s.
    /// Wildcard-free locations yield a single descriptor verbatim; glob patterns
    /// are resolved against <paramref name="fileSystem"/> (results ordered by
    /// virtual path for deterministic ingestion). A glob that matches nothing
    /// yields nothing — callers decide whether an empty result is an error.
    /// </summary>
    /// <param name="fileSystem">Virtual file system used to enumerate candidates.</param>
    /// <param name="locations">Virtual paths and/or glob patterns (e.g. <c>/workspace/docs/**/*.md</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<IReadOnlyList<SourceDescriptor>> ExpandAsync(
        IFileSystemService fileSystem,
        IEnumerable<string> locations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(locations);

        var descriptors = new List<SourceDescriptor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var location in locations)
        {
            if (string.IsNullOrWhiteSpace(location))
                continue;

            if (!HasWildcard(location))
            {
                if (seen.Add(location))
                    descriptors.Add(new SourceDescriptor { Location = location });
                continue;
            }

#pragma warning disable S3267 // async enumeration with a side-effecting dedupe; Where(seen.Add) would hide the mutation
            foreach (var match in await ExpandPatternAsync(fileSystem, location, cancellationToken).ConfigureAwait(false))
            {
                if (seen.Add(match))
                    descriptors.Add(new SourceDescriptor { Location = match });
            }
#pragma warning restore S3267
        }

        return descriptors;
    }

    private static async Task<IReadOnlyList<string>> ExpandPatternAsync(
        IFileSystemService fileSystem, string pattern, CancellationToken ct)
    {
        var (root, relativePattern) = SplitAtFirstWildcardSegment(pattern);
        var regex = GlobToRegex(relativePattern);

        var matches = new List<string>();
        try
        {
            await foreach (var entry in fileSystem
                .EnumerateFilesAsync(root, new VirtualEnumerationOptions(Recursive: true), ct)
                .ConfigureAwait(false))
            {
                if (entry.Kind != VirtualEntryKind.File)
                    continue;

                var prefix = root.TrimEnd('/') + "/";
                if (!entry.VirtualPath.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                var relative = entry.VirtualPath[prefix.Length..];
                if (regex.IsMatch(relative))
                    matches.Add(entry.VirtualPath);
            }
        }
        catch (DirectoryNotFoundException)
        {
            // A glob whose literal base does not exist simply matches nothing —
            // callers decide whether an empty expansion is an error.
        }
        catch (FileNotFoundException)
        {
            // Same contract as above for providers that report a missing root as file-not-found.
        }

        matches.Sort(StringComparer.Ordinal);
        return matches;
    }

    /// <summary>
    /// Splits a glob pattern into its literal base directory (segments before the
    /// first wildcard-bearing segment) and the remaining relative pattern.
    /// <c>/workspace/docs/**/*.md</c> → (<c>/workspace/docs</c>, <c>**/*.md</c>).
    /// </summary>
    internal static (string Root, string RelativePattern) SplitAtFirstWildcardSegment(string pattern)
    {
        var normalized = pattern.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var literal = new List<string>();
        var index = 0;
        while (index < segments.Length && segments[index].IndexOfAny(WildcardChars) < 0)
        {
            literal.Add(segments[index]);
            index++;
        }

        var root = "/" + string.Join('/', literal);
        var relative = string.Join('/', segments[index..]);
        return (root, relative);
    }

    /// <summary>
    /// Translates a relative glob into an anchored regex over <c>/</c>-separated
    /// virtual paths: <c>**/</c> spans any directory depth (including none),
    /// <c>*</c> stays within one segment, <c>?</c> matches one non-separator char.
    /// </summary>
    internal static Regex GlobToRegex(string relativePattern)
    {
        var sb = new StringBuilder("^");
        var i = 0;
        while (i < relativePattern.Length)
            i += AppendToken(sb, relativePattern, i);

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Appends the regex translation of the glob token starting at
    /// <paramref name="index"/> and returns how many pattern characters it consumed.
    /// </summary>
    private static int AppendToken(StringBuilder sb, string pattern, int index)
    {
        var c = pattern[index];
        if (c == '*' && IsCharAt(pattern, index + 1, '*'))
            return AppendDoubleStar(sb, pattern, index);

        sb.Append(c switch
        {
            '*' => "[^/]*",
            '?' => "[^/]",
            _ => Regex.Escape(c.ToString()),
        });
        return 1;
    }

    /// <summary>
    /// Appends the translation of a <c>**</c> token: <c>**/</c> spans any directory
    /// depth including none, a trailing <c>**</c> spans anything.
    /// </summary>
    private static int AppendDoubleStar(StringBuilder sb, string pattern, int index)
    {
        var followedBySlash = IsCharAt(pattern, index + 2, '/');
        sb.Append(followedBySlash ? "(?:.*/)?" : ".*");
        return followedBySlash ? 3 : 2;
    }

    private static bool IsCharAt(string pattern, int index, char expected)
        => index < pattern.Length && pattern[index] == expected;
}
