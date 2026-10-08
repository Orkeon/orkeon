using System.ComponentModel;
using System.Diagnostics;

namespace Orkeon.Tests.Shared.Produced;

/// <summary>
/// The files of the repository a test judges as a set — every settings file, every page of the
/// documentation —, listed without crawling what the repository does not hold: git says which files
/// it tracks and which are new, in one call; where git cannot answer (no git, an export without its
/// history), the disk is walked, the build outputs left aside.
/// </summary>
public static class RepositoryFiles
{
    /// <summary>The folders a build or a package manager fills: none of their files is a source.</summary>
    private static readonly string[] s_outputs = ["bin", "obj", "obj-linux", "node_modules", "packages", ".git"];

    /// <summary>
    /// What the index holds and what is new and not ignored: a file just written is judged before it
    /// is added, and what <c>.gitignore</c> sets aside — build outputs, a clone's own folders — is not walked.
    /// </summary>
    private static readonly string[] s_list = ["ls-files", "-z", "--cached", "--others", "--exclude-standard", "--"];

    /// <summary>
    /// The files of the repository under <paramref name="folders"/> that <paramref name="matches"/>
    /// keeps, by their path from the repository's root with forward slashes, sorted.
    /// </summary>
    /// <param name="folders">Folders from the repository's root, with forward slashes; <c>.</c> for the root itself.</param>
    /// <param name="matches">Whether a file is one of the set, given its path from the root.</param>
    public static IReadOnlyList<string> Under(IEnumerable<string> folders, Func<string, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(matches);

        var root = ProducedFile.RepositoryRoot();
        var asked = folders.ToList();
        var listed = Tracked(root, asked) ?? OnDisk(root, asked);
        return [.. listed.Where(matches).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>What git knows under the folders — tracked, or new and not ignored —, or null when git cannot say.</summary>
    private static List<string>? Tracked(string root, List<string> folders)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in s_list.Concat(folders))
            start.ArgumentList.Add(argument);

        string output;
        try
        {
            using var git = Process.Start(start);
            if (git is null)
                return null;

            var errors = git.StandardError.ReadToEndAsync();
            output = git.StandardOutput.ReadToEnd();
            git.WaitForExit();
            _ = errors.Result;
            if (git.ExitCode != 0)
                return null;
        }
        catch (Win32Exception)
        {
            return null;
        }

        var files = output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            // A file removed from the working copy and not yet from the index is not there to read.
            .Where(file => File.Exists(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar))))
            .ToList();
        return files.Count == 0 ? null : files;
    }

    private static List<string> OnDisk(string root, List<string> folders)
    {
        var files = new List<string>();
        foreach (var folder in folders)
        {
            var physical = Path.GetFullPath(Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(physical))
                files.Add(FromRoot(root, physical));
            else if (Directory.Exists(physical))
                Walk(root, physical, files);
        }

        return files;
    }

    private static void Walk(string root, string directory, List<string> files)
    {
        files.AddRange(Directory.EnumerateFiles(directory).Select(file => FromRoot(root, file)));
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (!s_outputs.Contains(Path.GetFileName(child), StringComparer.Ordinal))
                Walk(root, child, files);
        }
    }

    private static string FromRoot(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
}
