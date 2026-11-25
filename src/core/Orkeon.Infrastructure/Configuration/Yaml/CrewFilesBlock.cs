using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Orkeon.Domain.Configuration;
using Orkeon.Infrastructure.Serialization;
using YamlDotNet.Core;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// The <c>files:</c> block of a crew: its mapping, its rules, and the probe that reads it alone, without loading the crew.
/// </summary>
internal static class CrewFilesBlock
{
    /// <summary>What the file beside a script is named after: <c>support.ork.ts</c> has <c>support.files.yaml</c>.</summary>
    private const string NeighbourSuffix = ".files.yaml";

    private static readonly string[] s_scriptSuffixes = [".ork.ts", ".ork.js"];

    /// <summary>What a declaration says, or why it cannot be read.</summary>
    /// <param name="Folders">The folders declared; null when there is no block, or when it is invalid.</param>
    /// <param name="Fault">Why the declaration is invalid; null when it is not.</param>
    internal sealed record Declaration(IReadOnlyList<CrewFileFolder>? Folders, string? Fault);

    /// <summary>The file beside a <c>.ork.ts</c> or <c>.ork.js</c> script that declares its folders; null for another path.</summary>
    public static string? NeighbourOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var suffix = Array.Find(s_scriptSuffixes, s => path.EndsWith(s, StringComparison.OrdinalIgnoreCase));
        return suffix is null ? null : path[..^suffix.Length] + NeighbourSuffix;
    }

    /// <summary>The block as the crew holds it; null when the crew wrote none.</summary>
    /// <param name="yaml">The block as read.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1168:Empty arrays and collections should be returned instead of null",
        Justification = "null means the crew wrote no files: block, an empty list that it wrote an empty one: the exporter tells them apart.")]
    public static List<CrewFileFolder>? Map(Collection<CrewFileYamlConfig>? yaml)
    {
        if (yaml is null)
            return null;

        var folders = new List<CrewFileFolder>(yaml.Count);
        for (var i = 0; i < yaml.Count; i++)
        {
            // An empty item (a lone "-") is read as null.
            CrewFileYamlConfig? item = yaml[i];
            if (!CrewFileFolder.TryNormalizePath(item?.Path, out var path, out var refusal))
                throw new InvalidOperationException($"files[{i}]: '{item?.Path}' is refused: {refusal}.");

            folders.Add(new CrewFileFolder { Path = path, Download = item?.Download ?? false, Upload = item?.Upload ?? false });
        }

        return folders;
    }

    /// <summary>The faults of a block, one sentence each; empty when it can be served.</summary>
    /// <param name="folders">The folders declared.</param>
    public static IEnumerable<string> Faults(IReadOnlyList<CrewFileFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        // Two spellings of one folder on a file system that folds case are one folder listed twice.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            if (!CrewFileFolder.TryNormalizePath(folder.Path, out var path, out var refusal))
                yield return $"files: '{folder.Path}' is refused: {refusal}.";
            else if (path != folder.Path)
                yield return $"files: '{folder.Path}' is written '{path}'.";

            if (!folder.Download && !folder.Upload)
                yield return $"files: '{folder.Path}' opens nothing: write 'download: true', 'upload: true', or both.";
            if (!seen.Add(folder.Path))
                yield return $"files: '{folder.Path}' is listed twice.";
        }
    }

    /// <summary>The block of a crew's YAML; its other keys are the loader's to judge.</summary>
    /// <param name="yaml">The text of the crew file, or of the settings file of a crew directory.</param>
    /// <param name="file">The virtual path it was read from.</param>
    public static Declaration ReadCrew(string yaml, string file) => Read(yaml, file, ownFile: false);

    /// <summary>The block of the file beside a script, which holds nothing else.</summary>
    /// <param name="yaml">The text of the file.</param>
    /// <param name="file">The virtual path it was read from.</param>
    public static Declaration ReadNeighbour(string yaml, string file) => Read(yaml, file, ownFile: true);

    private static Declaration Read(string yaml, string file, bool ownFile)
    {
        if (string.IsNullOrWhiteSpace(yaml))
            return new Declaration(null, null);

        try
        {
            var expanded = YamlAnchorPreprocessor.Expand(yaml);
            var faults = ownFile
                ? CrewYamlKeyCheck.Find(expanded, file, typeof(CrewFilesYamlConfig), string.Empty)
                : CrewYamlKeyCheck.FindKey(expanded, file, typeof(CrewFilesYamlConfig), "files");
            if (faults.Count > 0)
                return new Declaration(null, string.Join(" ", faults));

            var folders = Map(new YamlDotNetSerializer().Deserialize<CrewFilesYamlConfig>(expanded.Text)?.Files);
            if (folders is null)
                return new Declaration(null, null);

            string[] wrong = [.. Faults(folders)];
            return wrong.Length == 0
                ? new Declaration(folders, null)
                : new Declaration(null, $"{file}: {string.Join(" ", wrong)}");
        }
        catch (Exception ex) when (ex is YamlException or InvalidOperationException or RegexMatchTimeoutException or ArgumentException)
        {
            return new Declaration(null, $"{file}: {(ex.InnerException ?? ex).Message}");
        }
    }
}
