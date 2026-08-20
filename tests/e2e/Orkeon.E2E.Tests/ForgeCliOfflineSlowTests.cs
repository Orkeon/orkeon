using System.Diagnostics;

namespace Orkeon.E2E.Tests;

/// <summary>
/// FORGE-08: the real `orkeon forge` CLI, spawned as a process, exercised on its fully
/// offline half — `list`, the loud refusals, and `promote` on the bundled ready session
/// (examples/forge/promote-demo). No LLM, no key, no network: the interview and the
/// sandboxed try belong to the owner-side recipe. Category=Slow: each invocation is a
/// `dotnet run` of the CLI.
/// </summary>
[Trait("Category", "Slow")]
public sealed class ForgeCliOfflineSlowTests : IDisposable
{
    private static readonly string RepoRoot = FindRepoRoot();

    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "orkeon-e2e-forge-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
            Directory.Delete(_scratch, recursive: true);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Orkeon.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Orkeon.sln not found above the test base directory.");
    }

    private static (int ExitCode, string Output) RunCli(string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project {Path.Combine(RepoRoot, "src", "scripting", "Orkeon.Scripting.Cli")} -- {arguments}",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit((int)TimeSpan.FromMinutes(12).TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"CLI did not exit. Output so far:\n{stdout}\n{stderr}");
        }

        return (process.ExitCode, stdout + "\n" + stderr);
    }

    /// <summary>A private copy of the bundled demo workspace — promote mutates the session.</summary>
    private string CopyDemoWorkspace()
    {
        var source = Path.Combine(RepoRoot, "examples", "forge", "promote-demo");
        CopyTree(source, _scratch);
        return _scratch;
    }

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
            CopyTree(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    [Fact]
    public void List_promote_and_the_promoted_folder_hold_the_offline_contract()
    {
        var workspace = CopyDemoWorkspace();

        var (listCode, listOutput) = RunCli("forge list", workspace);
        Assert.True(listCode == 0, $"forge list exited {listCode}. Output:\n{listOutput}");
        Assert.Contains("supplier-watch", listOutput, StringComparison.Ordinal);
        Assert.Contains("Ready", listOutput, StringComparison.Ordinal);

        var destination = Path.Combine(workspace, "my-solution");
        var (promoteCode, promoteOutput) = RunCli(
            $"forge promote supplier-watch --to {destination} --schedule daily@07:30 --events jsonl",
            workspace);
        Assert.True(promoteCode == 0, $"forge promote exited {promoteCode}. Output:\n{promoteOutput}");
        Assert.Contains("\"kind\":\"promoted\"", promoteOutput, StringComparison.Ordinal);

        // The folder is the ordinary artifact the docs promise.
        Assert.True(File.Exists(Path.Combine(destination, "FORGE.md")));
        Assert.True(File.Exists(Path.Combine(destination, "run.sh")));
        Assert.True(File.Exists(Path.Combine(destination, "crew", "config.yaml")));
        Assert.Contains("30 7 * * *",
            File.ReadAllText(Path.Combine(destination, "schedule", "cron.txt")), StringComparison.Ordinal);
        Assert.Contains("The summary cites its sources",
            File.ReadAllText(Path.Combine(destination, "FORGE.md")), StringComparison.Ordinal);

        // Promotion is recorded; a second one is refused by design.
        var (againCode, againOutput) = RunCli($"forge promote supplier-watch --to {destination}-2", workspace);
        Assert.Equal(1, againCode);
        Assert.Contains("only a ready session", againOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void The_cycle_refuses_loudly_without_an_llm_and_on_a_bad_grammar()
    {
        Directory.CreateDirectory(_scratch);

        // No Llm section anywhere under the scratch workspace: the interview refuses with
        // the remedy instead of babbling through an echo provider.
        var settings = Path.Combine(_scratch, "empty-settings.json");
        File.WriteAllText(settings, "{}");
        var (code, output) = RunCli($"forge \"a need\" --settings {settings}", _scratch);
        Assert.Equal(1, code);
        Assert.Contains("FORGE-LLM-UNAVAILABLE", output, StringComparison.Ordinal);

        var (badCode, badOutput) = RunCli("forge promote ghost --to somewhere", _scratch);
        Assert.Equal(1, badCode);
        Assert.Contains("ghost", badOutput, StringComparison.Ordinal);
    }
}
