using Orkeon.Constants.FileSystem;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// Where a mount the runner adds lands against what <c>appsettings.json</c> declares: by
/// virtual root (STUDIO-15 D-01). A <c>--mount</c> on a root the settings already declare is
/// written at that entry's index and replaces it for the run; a <c>--mount</c> on a new root
/// is appended after the highest declared index; internal mounts keep appending.
/// <para>
/// Two earlier rules each failed one way. Writing from index 0 replaced the operator's first
/// entry on every run — the crew mount is always there — and nothing said a mount had been
/// dropped. Appending unconditionally kept every entry and produced "Duplicate virtual paths"
/// out of a DI factory the moment a settings entry and a <c>--mount</c> named the same root,
/// which is exactly what Studio's team flow produces by design: a team associates a folder
/// the settings already declare, verbatim. These tests read the merged registry and the
/// bound configuration rather than the strings the runner produces, which is what the
/// earlier suites looked at and why none of them could see either failure.
/// </para>
/// </summary>
public sealed class MountOverrideByNameTests : IDisposable
{
    private readonly string _root;

    public MountOverrideByNameTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-mount-by-name-" + Guid.NewGuid().ToString("N"));
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

    private static string Spec(string directory, string virtualPath, string rights) =>
        $"{FileSystemMount.Quote(directory)}:{virtualPath}:{rights}";

    private static string JsonArray(params string[] values) =>
        string.Join(", ", values.Select(v => System.Text.Json.JsonSerializer.Serialize(v)));

    /// <summary>A settings file declaring the given mounts, RaggableTree off so no model loads.</summary>
    private string WriteSettings(string[] mounts, string[]? internalMounts = null)
    {
        var settingsPath = Path.Combine(_root, "appsettings.json");
        var internalSection = internalMounts is null
            ? ""
            : $", \"InternalMounts\": [ {JsonArray(internalMounts)} ]";
        File.WriteAllText(settingsPath,
            "{ \"RaggableTree\": { \"Enabled\": false }, \"Orkeon\": { \"FileSystem\": { \"Mounts\": [ "
            + JsonArray(mounts) + " ]" + internalSection + " } } }");
        return settingsPath;
    }

    private static IReadOnlyDictionary<string, FileSystemMount> MountsByRoot(Microsoft.Extensions.Hosting.IHost host) =>
        host.Services.GetRequiredService<FileSystemRegistry>()
            .GetMounts()
            .ToDictionary(m => m.VirtualPath, StringComparer.Ordinal);

    [Fact]
    public void A_mount_on_a_root_the_settings_declare_replaces_that_entry_by_name()
    {
        var data = SubDir("data");
        var shared = SubDir("shared");
        var crew = SubDir("crew");
        var other = SubDir("other");
        var settingsPath = WriteSettings([Spec(data, "/data", "ro"), Spec(shared, "/shared", "ro")]);

        using var logs = new CapturingLoggerProvider();
        using var host = RunnerHost.Build(
            settingsPath,
            new RunnerMountPlan { CliMounts = [Spec(crew, "/crew", "ro"), Spec(other, "/data", "rw")] },
            configureLogging: (_, b) => { b.AddProvider(logs); b.SetMinimumLevel(LogLevel.Information); });

        // One /data, the run's own — not the settings entry, and not a duplicate of it.
        var mounts = MountsByRoot(host);
        Assert.Equal(other, mounts["/data"].BasePath);
        Assert.Equal(FileAccessRights.ReadWrite, mounts["/data"].DefaultRights);
        Assert.Equal(shared, mounts["/shared"].BasePath);
        Assert.Equal(crew, mounts["/crew"].BasePath);

        // Written at the settings entry's own index; the untouched entry keeps its index.
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        Assert.Equal(Spec(other, "/data", "rw"), configuration["Orkeon:FileSystem:Mounts:0"]);
        Assert.Equal(Spec(shared, "/shared", "ro"), configuration["Orkeon:FileSystem:Mounts:1"]);

        // And the log says which default this run overrode, by root name.
        Assert.Contains(logs.Entries, e =>
            e.Level == LogLevel.Information && e.Message == "mount /data: --mount replaces the settings entry");
        Assert.DoesNotContain(logs.Entries, e => e.Message.Contains("/shared", StringComparison.Ordinal));
    }

    [Fact]
    public void A_mount_on_a_new_root_is_appended_after_the_declared_entries()
    {
        var data = SubDir("data");
        var shared = SubDir("shared");
        var crew = SubDir("crew");
        var extra = SubDir("extra");
        var settingsPath = WriteSettings([Spec(data, "/data", "ro"), Spec(shared, "/shared", "ro")]);

        using var host = RunnerHost.Build(
            settingsPath,
            new RunnerMountPlan { CliMounts = [Spec(crew, "/crew", "ro"), Spec(extra, "/extra", "rw")] });

        // The operator's, both of them — /data is the one that used to vanish under the
        // index-0 rule — alongside what the run added.
        var mounts = MountsByRoot(host);
        Assert.Equal(data, mounts["/data"].BasePath);
        Assert.Equal(shared, mounts["/shared"].BasePath);
        Assert.Equal(crew, mounts["/crew"].BasePath);
        Assert.Equal(extra, mounts["/extra"].BasePath);

        var configuration = host.Services.GetRequiredService<IConfiguration>();
        Assert.Equal(Spec(crew, "/crew", "ro"), configuration["Orkeon:FileSystem:Mounts:2"]);
        Assert.Equal(Spec(extra, "/extra", "rw"), configuration["Orkeon:FileSystem:Mounts:3"]);
    }

    /// <summary>
    /// The runner inserts the crew mount first in the command-line list, and that list is
    /// placed after the declared entries — so <c>/crew</c> never takes a settings entry's
    /// index. It cannot replace one by name either: a settings file claiming <c>/crew</c> is
    /// refused by <see cref="RunnerExecution.EnsureReservedRootsAreFree"/> before any host.
    /// </summary>
    [Fact]
    public void The_crew_mount_is_still_inserted_first_and_never_replaces_a_settings_entry()
    {
        var data = SubDir("data");
        var crewDir = SubDir("crew");
        var configPath = Path.Combine(crewDir, "config.yaml");
        File.WriteAllText(configPath, MinimalCrewYaml);
        var settingsPath = WriteSettings([Spec(data, "/data", "ro")]);

        var opts = new TestOptions { ConfigPath = configPath, SettingsPath = settingsPath, AllowExternalMounts = true };
        Assert.True(RunnerExecution.TryBuildHost(opts, "Orkeon.Hosting.Tests", null, out var bootstrap, out _));
        using var host = bootstrap!.Host;

        Assert.StartsWith(FileSystemMount.Quote(crewDir) + ":", bootstrap.CliMounts[0], StringComparison.Ordinal);
        var configuration = host.Services.GetRequiredService<IConfiguration>();
        Assert.Equal(Spec(data, "/data", "ro"), configuration["Orkeon:FileSystem:Mounts:0"]);
        Assert.Equal(bootstrap.CliMounts[0], configuration["Orkeon:FileSystem:Mounts:1"]);

        var mounts = MountsByRoot(host);
        Assert.Equal(data, mounts["/data"].BasePath);
        Assert.Equal(crewDir, mounts[RunnerVirtualRoots.Crew].BasePath);
    }

    [Fact]
    public void Internal_mounts_keep_appending()
    {
        var vault = SubDir("vault");
        var crew = SubDir("crew");
        var logs = SubDir("logs");
        var settingsPath = WriteSettings([], internalMounts: [Spec(vault, "/vault", "rw")]);

        using var host = RunnerHost.Build(
            settingsPath,
            new RunnerMountPlan
            {
                CliMounts = [Spec(crew, "/crew", "ro")],
                InternalMounts = [Spec(logs, RunnerVirtualRoots.LlmLogs, "rw")],
            });

        var mounts = MountsByRoot(host);
        Assert.Equal(vault, mounts["/vault"].BasePath);
        Assert.Equal(logs, mounts[RunnerVirtualRoots.LlmLogs].BasePath);

        var configuration = host.Services.GetRequiredService<IConfiguration>();
        Assert.Equal(Spec(vault, "/vault", "rw"), configuration["Orkeon:FileSystem:InternalMounts:0"]);
        Assert.Equal(Spec(logs, RunnerVirtualRoots.LlmLogs, "rw"), configuration["Orkeon:FileSystem:InternalMounts:1"]);
    }

    /// <summary>
    /// STUDIO-12 C4. A folder declared in the settings — outside the process working directory,
    /// as an operator's data folder usually is — was mounted and then refused on every access:
    /// <c>PathValidator</c> only knows the working directory and the whitelist, and
    /// <c>--allow-external-mounts</c> only ever whitelists the <c>--mount</c> arguments. A
    /// declared mount is the machine owner's explicit intent, so its base is whitelisted
    /// without any flag; a <c>--mount</c> without the flag stays exactly as gated as before.
    /// </summary>
    [Fact]
    public void A_settings_declared_mount_outside_the_cwd_is_whitelisted_for_the_path_validator()
    {
        var declared = SubDir("declared");
        var extra = SubDir("extra");
        var crew = SubDir("crew");
        File.WriteAllText(Path.Combine(declared, "invoice.txt"), "x");
        File.WriteAllText(Path.Combine(extra, "note.txt"), "x");
        Assert.False(PhysicalPathContainment.IsUnder(declared, Directory.GetCurrentDirectory()));
        var settingsPath = WriteSettings([Spec(declared, "/data", "ro")]);

        using var host = RunnerHost.Build(
            settingsPath,
            new RunnerMountPlan { CliMounts = [Spec(crew, "/crew", "ro"), Spec(extra, "/extra", "ro")] });

        var whitelist = host.Services.GetRequiredService<IOptions<PathSecurityOptions>>().Value.AdditionalAllowedDirectories;
        Assert.Contains(declared, whitelist);
        Assert.DoesNotContain(extra, whitelist);

        var fileSystem = host.Services.GetRequiredService<IFileSystemService>();
        Assert.True(fileSystem.ResolveAndValidate("/data/invoice.txt", FileAccessRights.Read).IsAllowed);
        Assert.False(fileSystem.ResolveAndValidate("/extra/note.txt", FileAccessRights.Read).IsAllowed);
    }

    private sealed class TestOptions : RunnerOptionsBase { }

    private const string MinimalCrewYaml = """
        name: "by-name"
        goal: "Crew that never runs: the host is built and inspected"
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
        """;
}

/// <summary>
/// The pre-checks and the fault barrier around the by-name rule (STUDIO-15 D-02, D-03), on the
/// model of <see cref="MountSourceExistenceGuardTests"/>. Joins
/// <see cref="ConsoleSerialCollection"/> because every test redirects the process-global
/// <see cref="Console"/> streams.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class VirtualRootUniquenessGuardTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private readonly string _root;

    public VirtualRootUniquenessGuardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-root-unique-" + Guid.NewGuid().ToString("N"));
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

    private string WriteCrew()
    {
        var crewDir = SubDir("crew");
        var configPath = Path.Combine(crewDir, "config.yaml");
        File.WriteAllText(configPath, """
            name: "root-unique"
            goal: "Crew that never runs"
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
            """);
        // Next to the crew, so the resolution chain finds it without a WARNING line and the
        // RaggableTree model never loads.
        File.WriteAllText(Path.Combine(crewDir, "appsettings.json"), "{ \"RaggableTree\": { \"Enabled\": false } }");
        return configPath;
    }

    private static async Task<(int exit, string stdout, string stderr)> CaptureAsync(Func<Task<int>> run)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        using var stdout = new StringWriter(new StringBuilder());
        using var stderr = new StringWriter(new StringBuilder());
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await run().ConfigureAwait(false);
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

    [Fact]
    public async Task Two_command_line_mounts_on_one_root_are_refused_before_any_host_boots()
    {
        var configPath = WriteCrew();
        var a = SubDir("a");
        var b = SubDir("b");
        var opts = new TestOptions
        {
            ConfigPath = configPath,
            AllowExternalMounts = true,
            Mounts = [$"{a}:/x:ro", $"{b}:/x:rw"],
        };

        var (exit, _, stderr) = await CaptureAsync(
            () => RunnerExecution.RunOneShotAsync(opts, "Orkeon.Hosting.Tests", externalCt: TestContext.Current.CancellationToken));

        Assert.Equal(1, exit);
        var line = Assert.Single(Lines(stderr));
        Assert.Equal($"ERROR: '/x' is mounted twice on the command line: {a}:/x:ro and {b}:/x:rw. Keep one.", line);
        Assert.DoesNotContain("Using settings", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Duplicate virtual paths", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_settings_file_declaring_one_root_twice_is_refused_with_the_file_named()
    {
        var configPath = WriteCrew();
        var a = SubDir("a");
        var b = SubDir("b");
        var settingsPath = Path.Combine(_root, "appsettings.json");
        await File.WriteAllTextAsync(settingsPath,
            "{ \"RaggableTree\": { \"Enabled\": false }, \"Orkeon\": { \"FileSystem\": { \"Mounts\": [ "
            + System.Text.Json.JsonSerializer.Serialize($"{a}:/x:ro") + ", "
            + System.Text.Json.JsonSerializer.Serialize($"{b}:/x:rw") + " ] } } }",
            TestContext.Current.CancellationToken);
        var opts = new TestOptions { ConfigPath = configPath, SettingsPath = settingsPath, AllowExternalMounts = true };

        var (exit, _, stderr) = await CaptureAsync(
            () => RunnerExecution.RunOneShotAsync(opts, "Orkeon.Hosting.Tests", externalCt: TestContext.Current.CancellationToken));

        Assert.Equal(1, exit);
        var line = Assert.Single(Lines(stderr));
        Assert.Equal($"ERROR: '/x' is declared twice in {settingsPath}: {a}:/x:ro and {b}:/x:rw. Keep one.", line);
    }

    /// <summary>
    /// The settings × command-line case is the one the by-name rule exists for: not a
    /// duplicate, a replacement. The guard must let it through, or Studio's team flow — a
    /// team associates a folder the settings already declare — fails exactly as before.
    /// </summary>
    [Fact]
    public void A_settings_root_named_again_on_the_command_line_is_not_a_duplicate()
    {
        var a = SubDir("a");
        var settingsPath = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(settingsPath,
            "{ \"Orkeon\": { \"FileSystem\": { \"Mounts\": [ " + System.Text.Json.JsonSerializer.Serialize($"{a}:/x:ro") + " ] } } }");

        var origErr = Console.Error;
        using var stderr = new StringWriter(new StringBuilder());
        Console.SetError(stderr);
        bool ok;
        try
        {
            ok = RunnerExecution.EnsureVirtualRootsAreUnique([$"{a}:/x:rw", "not-a-mount"], settingsPath);
        }
        finally
        {
            Console.SetError(origErr);
        }

        Assert.True(ok);
        Assert.Empty(stderr.ToString());
    }

    /// <summary>
    /// D-03. An exception out of a DI factory at kickoff — the registry refusing a mount, a
    /// factory that cannot be built — used to be one Error log line of ~3000 characters whose
    /// only actionable words were the first ten, followed by nothing on stderr that said why
    /// exit code 2 happened. The message is now the LAST stderr line, and the stack trace is
    /// one <c>--verbose 2</c> away.
    /// </summary>
    [Fact]
    public async Task A_factory_failure_at_kickoff_ends_stderr_with_its_message_on_one_line()
    {
        var configPath = WriteCrew();
        var opts = new TestOptions { ConfigPath = configPath, AllowExternalMounts = true };

        var (exit, stdout, stderr) = await CaptureAsync(
            () => RunnerExecution.RunOneShotAsync(
                opts, "Orkeon.Hosting.Tests",
                configureServices: (_, services) => services.AddSingleton<Orkeon.Application.Interfaces.ICrewFactory>(
                    _ => throw new InvalidOperationException("Duplicate virtual paths: /workspace, /output")),
                externalCt: TestContext.Current.CancellationToken));

        Assert.Equal(2, exit);
        Assert.Equal("ERROR: Duplicate virtual paths: /workspace, /output", Lines(stderr)[^1]);
        Assert.DoesNotContain("   at ", stderr + stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("System.InvalidOperationException", stderr + stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_factory_failure_at_kickoff_keeps_its_stack_trace_at_verbose_2()
    {
        var configPath = WriteCrew();
        var opts = new TestOptions { ConfigPath = configPath, AllowExternalMounts = true, Verbose = 2 };

        var (exit, stdout, stderr) = await CaptureAsync(
            () => RunnerExecution.RunOneShotAsync(
                opts, "Orkeon.Hosting.Tests",
                configureServices: (_, services) => services.AddSingleton<Orkeon.Application.Interfaces.ICrewFactory>(
                    _ => throw new InvalidOperationException("boom at kickoff")),
                externalCt: TestContext.Current.CancellationToken));

        Assert.Equal(2, exit);
        Assert.Equal("ERROR: boom at kickoff", Lines(stderr)[^1]);
        Assert.Contains("   at ", stderr + stdout, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException", stderr + stdout, StringComparison.Ordinal);
    }
}
