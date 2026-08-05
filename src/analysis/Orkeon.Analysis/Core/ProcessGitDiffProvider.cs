using System.Diagnostics;
using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Analysis.Core;

public sealed class ProcessGitDiffProvider : IGitDiffProvider
{
    // OUT-OF-SCOPE: resolving a dev-tool binary (git) on the host filesystem is outside VFS scope.
    // We probe a fixed allowlist of canonical paths to avoid executing arbitrary code placed
    // ahead of git on PATH by a compromised environment. Falls back to PATH-based lookup only
    // when none of the canonical paths exist (e.g., nix/asdf custom installs).
    private static readonly string[] GitCandidates =
    [
        "/usr/bin/git",
        "/usr/local/bin/git",
        "/opt/homebrew/bin/git",
        @"C:\Program Files\Git\cmd\git.exe",
        @"C:\Program Files\Git\bin\git.exe",
    ];

    private readonly string _gitPath = ResolveGitPath();

    [Orkeon.Compliance.Vfs.SuppressVfsCompliance("OUT-OF-SCOPE: probing host filesystem for git dev-tool binary; not subject to VFS policy")]
    private static string ResolveGitPath()
    {
        // OUT-OF-SCOPE: File.Exists here probes the host for the git dev-tool binary (see class note).
        return GitCandidates.FirstOrDefault(File.Exists)
            ?? "git"; // last resort: rely on PATH (acceptable for dev-tool-only scenarios)
    }

    public async Task<IReadOnlyList<string>> GetChangedFilesAsync(
        string rootPath, string fromCommit, string toCommit, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        ArgumentException.ThrowIfNullOrEmpty(fromCommit);
        ArgumentException.ThrowIfNullOrEmpty(toCommit);

        var psi = new ProcessStartInfo(_gitPath, $"diff --name-only {fromCommit} {toCommit}")
        {
            WorkingDirectory = rootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);

        if (process.ExitCode != 0) return [];

        var files = new List<string>();
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            files.Add(Path.Combine(rootPath, trimmed.Replace('/', Path.DirectorySeparatorChar)));
        }
        return files;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetWorkingTreeChangesAsync(string rootPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(rootPath);

        // Porcelain v1: two status columns + path; `-uall` lists untracked FILES (a new
        // module the agent just created is exactly what a search must see). Renames
        // ("R  old -> new") keep the NEW path — the old one has no content to index.
        var psi = new ProcessStartInfo(_gitPath, "status --porcelain=v1 -uall")
        {
            WorkingDirectory = rootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);

        // Not a repo (or git absent): nothing to report, never an error — the freshness
        // pass must degrade to the dirty-set alone, not take the search down.
        if (process.ExitCode != 0) return [];

        var files = new List<string>();
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4) continue;
            var path = line[3..].Trim();
            var arrow = path.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0) path = path[(arrow + 4)..];
            path = path.Trim('"'); // git quotes paths with spaces
            if (path.Length == 0) continue;
            files.Add(Path.Combine(rootPath, path.Replace('/', Path.DirectorySeparatorChar)));
        }
        return files;
    }
}
