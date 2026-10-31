using Orkeon.Domain.FileSystem;

namespace Orkeon.Analysis.Core;

/// <summary>
/// The ignore files of a tree, applied the way git applies them: <c>.git/info/exclude</c> first, then the
/// root <c>.gitignore</c>, then each nested <c>.gitignore</c> to the paths under its folder. The last pattern
/// that matches a path decides, so a deeper file overrides a shallower one, and a file under an ignored
/// folder stays ignored whatever a pattern says of the file itself.
/// </summary>
internal sealed class IgnoreFiles
{
    private const string InfoExclude = ".git/info/exclude";
    private const string GitignoreName = ".gitignore";

    private readonly List<Layer> _layers = [];

    /// <summary>The content of each ignore file that was read, by its path relative to the root.</summary>
    public Dictionary<string, string> Contents { get; } = new(StringComparer.Ordinal);

    private IgnoreFiles() { }

    /// <summary>
    /// Reads the ignore files of <paramref name="virtualRoot"/>: <c>.git/info/exclude</c>, the root
    /// <c>.gitignore</c> and the nested <c>.gitignore</c> files named by <paramref name="gitignoreRelativePaths"/>
    /// (relative to the root), shallowest first. A nested file whose own folder is already ignored is not read.
    /// A file the file system refuses to read counts as absent.
    /// </summary>
    public static async Task<IgnoreFiles> LoadAsync(
        IFileSystemService fs, string virtualRoot, IEnumerable<string> gitignoreRelativePaths, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(gitignoreRelativePaths);
        var files = new IgnoreFiles();
        await files.AddAsync(fs, virtualRoot, InfoExclude, prefix: string.Empty, ct).ConfigureAwait(false);
        var nested = gitignoreRelativePaths
            .Select(p => p.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p.Count(c => c == '/'))
            .ThenBy(p => p, StringComparer.Ordinal);
        foreach (var relative in nested)
        {
            ct.ThrowIfCancellationRequested();
            if (!string.Equals(Path.GetFileName(relative), GitignoreName, StringComparison.Ordinal)) continue;
            var prefix = relative.Length > GitignoreName.Length ? relative[..^(GitignoreName.Length + 1)] : string.Empty;
            if (prefix.Length > 0 && files.IsIgnored(prefix)) continue;
            await files.AddAsync(fs, virtualRoot, relative, prefix, ct).ConfigureAwait(false);
        }
        return files;
    }

    /// <summary>Whether <paramref name="relative"/> (a file path relative to the root) is ignored, through one of its folders or by itself.</summary>
    public bool IsIgnored(string relative)
    {
        var path = relative.Replace('\\', '/');
        var slash = -1;
        while ((slash = path.IndexOf('/', slash + 1)) >= 0)
        {
            if (Match(path[..slash], isDirectory: true) == true) return true;
        }
        return Match(path, isDirectory: false) == true;
    }

    private bool? Match(string path, bool isDirectory)
    {
        bool? result = null;
        foreach (var layer in _layers)
        {
            string sub;
            if (layer.Prefix.Length == 0) sub = path;
            else if (path.StartsWith(layer.Prefix + "/", StringComparison.Ordinal)) sub = path[(layer.Prefix.Length + 1)..];
            else continue;
            var matched = layer.Matcher.Match(sub, isDirectory);
            if (matched is not null) result = matched;
        }
        return result;
    }

    private async Task AddAsync(IFileSystemService fs, string virtualRoot, string relative, string prefix, CancellationToken ct)
    {
        string? text;
        try
        {
            text = await fs.TryReadAllTextAsync($"{virtualRoot}/{relative}", ct).ConfigureAwait(false);
        }
        catch (FileAccessDeniedException)
        {
            return;
        }
        if (text is null) return;
        Contents[relative] = text;
        _layers.Add(new Layer(prefix, new GitignoreMatcher(text.Split(['\r', '\n'], StringSplitOptions.None))));
    }

    private readonly record struct Layer(string Prefix, GitignoreMatcher Matcher);
}
