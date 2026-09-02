using System.Text;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// <see cref="RunnerExecution.EnsureMountSourcesExist"/> turns a mount whose host-side
/// directory is missing into a one-line actionable diagnostic. Without it the missing
/// directory only surfaces once the FileSystemRegistry DI factory validates the mount —
/// a raw <see cref="DirectoryNotFoundException"/> stack trace for a mkdir-sized mistake.
/// The console-capturing tests join <see cref="ConsoleSerialCollection"/> because they
/// redirect the process-global <see cref="Console"/> streams.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class MountSourceExistenceGuardTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private readonly string _tempDir;

    public MountSourceExistenceGuardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-hosting-mounts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    private static (bool ok, string stderr) CaptureGuard(Func<bool> run)
    {
        var origErr = Console.Error;
        using var stderr = new StringWriter(new StringBuilder());
        Console.SetError(stderr);
        try
        {
            return (run(), stderr.ToString());
        }
        finally
        {
            Console.SetError(origErr);
        }
    }

    [Fact]
    public void Guard_accepts_mounts_whose_source_directories_exist()
    {
        var existing = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(existing);

        var (ok, stderr) = CaptureGuard(() => RunnerExecution.EnsureMountSourcesExist(
            [$"{existing}:/output:rw"], settingsPath: null));

        Assert.True(ok);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Guard_refuses_a_mount_whose_source_directory_is_missing()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist");

        var (ok, stderr) = CaptureGuard(() => RunnerExecution.EnsureMountSourcesExist(
            [$"{missing}:/output:rw"], settingsPath: null));

        Assert.False(ok);
        Assert.Contains("ERROR: mount source directory does not exist", stderr, StringComparison.Ordinal);
        Assert.Contains(missing, stderr, StringComparison.Ordinal);
        Assert.Contains("mkdir", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_leaves_malformed_mount_strings_to_the_mount_parser()
    {
        // The parser reports malformed strings with its own precise message at host build
        // time; the guard must not preempt it (same stance as EnsureReservedRootsAreFree).
        var (ok, stderr) = CaptureGuard(() => RunnerExecution.EnsureMountSourcesExist(
            ["not-a-mount"], settingsPath: null));

        Assert.True(ok);
        Assert.Empty(stderr);
    }

    [Fact]
    public void Guard_checks_mounts_declared_in_the_settings_file()
    {
        var missing = Path.Combine(_tempDir, "declared-but-missing");
        var settingsPath = Path.Combine(_tempDir, "appsettings.json");
        File.WriteAllText(
            settingsPath,
            "{\"Orkeon\":{\"FileSystem\":{\"Mounts\":[\"" + missing + ":/data:ro\"]}}}");

        var (ok, stderr) = CaptureGuard(() => RunnerExecution.EnsureMountSourcesExist(
            [], settingsPath));

        Assert.False(ok);
        Assert.Contains("ERROR: mount source directory does not exist", stderr, StringComparison.Ordinal);
        Assert.Contains(missing, stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_shot_run_fails_cleanly_when_a_mount_source_is_missing()
    {
        // The guard runs inside TryBuildHost, before any host is built, so the runner
        // exits 1 with the diagnostic — not with a DI stack trace.
        var configPath = Path.Combine(_tempDir, "config.yaml");
        await File.WriteAllTextAsync(configPath, """
            name: "mount-guard"
            goal: "Crew that never runs: a mount source is missing"
            process: "sequential"
            agents:
              reader:
                role: "Reader"
                goal: "Read a file"
                backstory: "A minimal test agent."
                maxIter: 1
            tasks:
              do_read:
                description: "Read a file and report its contents."
                expectedOutput: "The file contents."
                agent: "reader"
            """, TestContext.Current.CancellationToken);

        var missing = Path.Combine(_tempDir, "out-not-created");
        var opts = new TestOptions
        {
            ConfigPath = configPath,
            AllowExternalMounts = true,
            Mounts = [$"{missing}:/output:rw"],
        };

        var origErr = Console.Error;
        using var stderr = new StringWriter(new StringBuilder());
        Console.SetError(stderr);
        int exit;
        try
        {
            exit = await RunnerExecution.RunOneShotAsync(
                opts, "Orkeon.Hosting.Tests", externalCt: TestContext.Current.CancellationToken);
        }
        finally
        {
            Console.SetError(origErr);
        }

        Assert.Equal(1, exit);
        Assert.Contains("ERROR: mount source directory does not exist", stderr.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("DirectoryNotFoundException", stderr.ToString(), StringComparison.Ordinal);
    }
}
