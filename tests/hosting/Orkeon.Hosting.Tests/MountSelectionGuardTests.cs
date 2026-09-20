using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.Common;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The one-shot runner end to end against the selection guard (VFS-90): a refusal is one
/// <c>ERROR:</c> line before "Using settings" with exit code 1; an acceptance builds the host
/// with the plan applied. A sentinel crew factory stops each accepted run right after the host
/// is built — the fault barrier's last line then proves which side of the guard the run was on
/// without executing a crew. Joins <see cref="ConsoleSerialCollection"/> because every test
/// redirects the process-global <see cref="Console"/> streams.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class MountSelectionGuardTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private const string Sentinel = "host built: the guards passed";

    private readonly string _root;

    public MountSelectionGuardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-mount-select-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private string WriteCrew(string? mountsBlock = null)
    {
        var crewDir = SubDir("crew");
        var configPath = Path.Combine(crewDir, "config.yaml");
        File.WriteAllText(configPath, $$"""
            name: "mount-select"
            goal: "Crew that never runs"
            process: "sequential"
            {{mountsBlock ?? string.Empty}}
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
            """);
        return configPath;
    }

    private string WriteSettings(params string[] mounts)
    {
        var settingsPath = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(settingsPath,
            "{ \"RaggableTree\": { \"Enabled\": false }, \"Orkeon\": { \"FileSystem\": { \"Mounts\": [ "
            + string.Join(", ", mounts.Select(m => System.Text.Json.JsonSerializer.Serialize(m))) + " ] } } }");
        return settingsPath;
    }

    private static async Task<(int exit, string stdout, string stderr)> RunAsync(TestOptions opts)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        using var stdout = new StringWriter(new StringBuilder());
        using var stderr = new StringWriter(new StringBuilder());
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await RunnerExecution.RunOneShotAsync(
                opts, "Orkeon.Hosting.Tests",
                configureServices: (_, services) => services.AddSingleton<Orkeon.Application.Interfaces.ICrewFactory>(
                    _ => throw new InvalidOperationException(Sentinel)),
                externalCt: TestContext.Current.CancellationToken).ConfigureAwait(false);
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    private static string[] Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();

    private static void AssertHostWasBuilt((int exit, string stdout, string stderr) run)
    {
        Assert.Equal(2, run.exit);
        Assert.Equal("ERROR: " + Sentinel, Lines(run.stderr)[^1]);
        Assert.Contains("Using settings", run.stderr, StringComparison.Ordinal);
    }

    /// <summary>D-04: two ids, no choice — one line, before "Using settings", naming both.</summary>
    [Fact]
    public async Task Two_entries_of_one_root_with_nothing_selecting_one_are_refused_in_one_line_before_using_settings()
    {
        var a = SubDir("a");
        var b = SubDir("b");
        var idA = MountId.Create();
        var idB = MountId.Create();
        var settingsPath = WriteSettings($"{idA}|{a}:/output:rw", $"{idB}|{b}:/output:rw");
        var opts = new TestOptions { ConfigPath = WriteCrew(), SettingsPath = settingsPath, AllowExternalMounts = true };

        var (exit, _, stderr) = await RunAsync(opts);

        Assert.Equal(1, exit);
        var line = Assert.Single(Lines(stderr));
        Assert.Equal(
            $"ERROR: '/output' is declared twice in {settingsPath} ({idA}: {a}, {idB}: {b}) and nothing selects one. "
            + "Pass --mount-id <id>, list '<id>|/output' under mounts: in the crew, "
            + "or pass --mount <folder>:/output:rw to replace them all.",
            line);
        Assert.DoesNotContain("Using settings", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_mount_id_resolves_the_selection_and_the_run_goes_ahead()
    {
        var a = SubDir("a");
        var b = SubDir("b");
        var idA = MountId.Create();
        var idB = MountId.Create();
        var settingsPath = WriteSettings($"{idA}|{a}:/output:rw", $"{idB}|{b}:/output:rw");
        var opts = new TestOptions
        {
            ConfigPath = WriteCrew(),
            SettingsPath = settingsPath,
            AllowExternalMounts = true,
            MountIds = [idB.ToString()],
        };

        AssertHostWasBuilt(await RunAsync(opts));
    }

    [Fact]
    public async Task The_crews_mounts_block_resolves_the_selection_without_any_flag()
    {
        var a = SubDir("a");
        var b = SubDir("b");
        var idA = MountId.Create();
        var idB = MountId.Create();
        var settingsPath = WriteSettings($"{idA}|{a}:/output:rw", $"{idB}|{b}:/output:rw");
        var opts = new TestOptions
        {
            ConfigPath = WriteCrew($"mounts:\n  - {idB}|/output"),
            SettingsPath = settingsPath,
            AllowExternalMounts = true,
        };

        AssertHostWasBuilt(await RunAsync(opts));
    }

    [Fact]
    public async Task A_mount_on_the_root_replaces_both_entries_and_the_run_goes_ahead()
    {
        var a = SubDir("a");
        var b = SubDir("b");
        var run = SubDir("run");
        var settingsPath = WriteSettings($"{MountId.Create()}|{a}:/output:rw", $"{MountId.Create()}|{b}:/output:rw");
        var opts = new TestOptions
        {
            ConfigPath = WriteCrew(),
            SettingsPath = settingsPath,
            AllowExternalMounts = true,
            Mounts = [$"{run}:/output:rw"],
        };

        AssertHostWasBuilt(await RunAsync(opts));
    }

    [Fact]
    public async Task A_root_the_crew_requires_that_nothing_provides_is_refused_in_one_line()
    {
        var settingsPath = WriteSettings();
        var opts = new TestOptions
        {
            ConfigPath = WriteCrew("mounts:\n  - /reports"),
            SettingsPath = settingsPath,
            AllowExternalMounts = true,
        };

        var (exit, _, stderr) = await RunAsync(opts);

        Assert.Equal(1, exit);
        Assert.Equal(
            "ERROR: the crew requires '/reports' (mounts: in its definition) and nothing provides it: run the team's "
            + $"launcher, declare a folder under /reports in {settingsPath}, or pass --mount <folder>:/reports:rw.",
            Assert.Single(Lines(stderr)));
    }

    [Fact]
    public async Task A_malformed_item_in_the_crews_mounts_block_is_refused_with_the_file_named()
    {
        var settingsPath = WriteSettings();
        var configPath = WriteCrew("mounts:\n  - reports");
        var opts = new TestOptions { ConfigPath = configPath, SettingsPath = settingsPath, AllowExternalMounts = true };

        var (exit, _, stderr) = await RunAsync(opts);

        Assert.Equal(1, exit);
        Assert.Equal(
            $"ERROR: crew mounts: entry 'reports' in {configPath} is neither '/root' nor '<ulid>|/root' "
            + "(a virtual root starts with '/', a mount id is the 26-character ULID a settings entry carries before its '|').",
            Assert.Single(Lines(stderr)));
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2W")]
    public async Task A_malformed_mount_id_is_refused_in_one_line(string raw)
    {
        var settingsPath = WriteSettings();
        var opts = new TestOptions
        {
            ConfigPath = WriteCrew(),
            SettingsPath = settingsPath,
            AllowExternalMounts = true,
            MountIds = [raw],
        };

        var (exit, _, stderr) = await RunAsync(opts);

        Assert.Equal(1, exit);
        Assert.Equal(
            $"ERROR: --mount-id '{raw}' is not a mount id: expected the 26 Crockford base32 characters "
            + "a settings entry carries before its '|' (0-9, A-Z without I, L, O, U).",
            Assert.Single(Lines(stderr)));
    }

    /// <summary>
    /// D-10: an entry the run does not mount is not probed, so the folder of the entry left
    /// aside may be gone without stopping the crew that selects the other one.
    /// </summary>
    [Fact]
    public async Task A_withdrawn_entry_whose_folder_is_missing_does_not_stop_the_run()
    {
        var a = SubDir("a");
        var gone = Path.Combine(_root, "gone");
        var idA = MountId.Create();
        var settingsPath = WriteSettings($"{idA}|{a}:/output:rw", $"{MountId.Create()}|{gone}:/output:rw");
        var opts = new TestOptions
        {
            ConfigPath = WriteCrew(),
            SettingsPath = settingsPath,
            AllowExternalMounts = true,
            MountIds = [idA.ToString()],
        };

        AssertHostWasBuilt(await RunAsync(opts));
    }
}
