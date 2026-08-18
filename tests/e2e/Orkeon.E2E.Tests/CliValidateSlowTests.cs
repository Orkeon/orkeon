using System.Diagnostics;

namespace Orkeon.E2E.Tests;

/// <summary>
/// PUB-17 T1 (CLI leg): the real `orkeon` CLI, spawned as a process from the
/// source checkout, dry-runs bundled examples end-to-end (settings resolution,
/// host build, crew load under strict tool resolution) — no LLM call, no key.
/// Category=Slow: each --validate builds the CLI and loads a full host, which
/// takes minutes on slow filesystems; the nightly integration workflow owns it.
/// </summary>
[Trait("Category", "Slow")]
public class CliValidateSlowTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Orkeon.sln")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("Orkeon.sln not found above the test base directory.");
    }

    private static (int ExitCode, string Output) RunCli(string arguments, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project src/scripting/Orkeon.Scripting.Cli -- {arguments}",
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"CLI did not exit within {timeout}. Output so far:\n{stdout}\n{stderr}");
        }

        return (process.ExitCode, stdout + "\n" + stderr);
    }

    [Fact]
    public void Validate_SingleYamlExample_ExitsZero()
    {
        var (exitCode, output) = RunCli(
            "run examples/01-enterprise/01-research-assistant/config.yaml --validate",
            TimeSpan.FromMinutes(12));

        Assert.True(exitCode == 0, $"Expected exit 0, got {exitCode}. Output:\n{output}");
        Assert.Contains("VALIDATION OK", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_MultiFileCrewDirectory_ExitsZero()
    {
        var (exitCode, output) = RunCli(
            "run examples/crew-multifile --validate",
            TimeSpan.FromMinutes(12));

        Assert.True(exitCode == 0, $"Expected exit 0, got {exitCode}. Output:\n{output}");
        Assert.Contains("VALIDATION OK", output, StringComparison.OrdinalIgnoreCase);
    }
}
