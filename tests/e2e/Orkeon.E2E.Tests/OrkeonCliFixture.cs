using System.Diagnostics;

namespace Orkeon.E2E.Tests;

/// <summary>
/// Builds the <c>orkeon</c> CLI once for the whole assembly, so the tests that spawn it can
/// run it without rebuilding.
/// <para>
/// The reason is a failure that took three full-suite runs to pin down. Every invocation used
/// <c>dotnet run --project …</c>, which builds before it runs. Two test classes doing that
/// concurrently made MSBuild write its own diagnostics onto the stream the test was reading,
/// and an assertion looking for the CLI's message found
/// <c>Microsoft.Common.CurrentVersion.targets</c> instead. It passed in isolation and failed
/// under load — the signature of a harness problem wearing a test's clothes.
/// </para>
/// <para>
/// Building once and running with <c>--no-build</c> removes the contention and most of the
/// wall time with it.
/// </para>
/// </summary>
public sealed class OrkeonCliFixture
{
    /// <summary>Collection name for the classes that spawn the CLI.</summary>
    public const string CollectionName = "orkeon-cli";

    /// <summary>Absolute path of the repository root (the directory holding Orkeon.sln).</summary>
    public string RepoRoot { get; }

    /// <summary>Absolute path of the CLI project.</summary>
    public string ProjectPath { get; }

    /// <summary>Builds the CLI, and fails loudly rather than letting every test fail obscurely.</summary>
    public OrkeonCliFixture()
    {
        RepoRoot = FindRepoRoot();
        ProjectPath = Path.Combine(RepoRoot, "src", "scripting", "Orkeon.Scripting.Cli");

        var (exitCode, stdout, stderr) = RunDotnet($"build \"{ProjectPath}\" --nologo -v:q", RepoRoot, TimeSpan.FromMinutes(10));
        if (exitCode != 0)
            throw new InvalidOperationException($"Building the orkeon CLI failed with exit code {exitCode}:\n{stdout}\n{stderr}");
    }

    /// <summary>
    /// Runs the built CLI with <paramref name="arguments"/>. Returns the exit code and the
    /// merged streams — no build output can reach them, which is the whole point.
    /// </summary>
    public (int ExitCode, string Output) RunCli(
        string arguments, string workingDirectory, TimeSpan? timeout = null)
    {
        var (exitCode, stdout, stderr) = RunCliSplit(arguments, workingDirectory, timeout);
        return (exitCode, stdout + "\n" + stderr);
    }

    /// <summary>
    /// Runs the built CLI keeping the two streams apart — the only way to assert stdout
    /// purity, which is the `--events` contract: protocol lines and nothing else.
    /// </summary>
    public (int ExitCode, string Stdout, string Stderr) RunCliSplit(
        string arguments, string workingDirectory, TimeSpan? timeout = null) =>
        RunDotnet(
            $"run --project \"{ProjectPath}\" --no-build -- {arguments}",
            workingDirectory,
            timeout ?? TimeSpan.FromMinutes(5));

    private static (int ExitCode, string Stdout, string Stderr) RunDotnet(
        string arguments, string workingDirectory, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"`dotnet {arguments}` did not exit within {timeout}. Output so far:\n{stdout.Result}\n{stderr.Result}");
        }

        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Orkeon.sln")))
            dir = Path.GetDirectoryName(dir);

        return dir ?? throw new InvalidOperationException("Orkeon.sln not found above the test base directory.");
    }
}

/// <summary>
/// Groups the CLI-spawning classes so they share one build and never run at the same time.
/// </summary>
[CollectionDefinition(OrkeonCliFixture.CollectionName)]
public sealed class OrkeonCliCollection : ICollectionFixture<OrkeonCliFixture>;
