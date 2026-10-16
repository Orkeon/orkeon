using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orkeon.Cli.Commands.Scripting.Registry;
using Orkeon.ConsoleApp.Tests.Fakes;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Domain.Common;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting;
using Orkeon.Infrastructure.FileSystem;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>
/// GAP-49, decision 3 - the REPL places and guards its mounts as the runners do. A <c>--mount</c> on
/// a root its settings declare takes that entry's place for the session; a mount the REPL cannot
/// honour - a root it keeps for itself, <c>/credentials</c>, a root claimed twice on the command line,
/// a root declared twice with nothing to choose one, a folder that is not there - is refused before
/// any host, with the lines <c>orkeon run</c> writes, and exit code 1, wherever it was declared. It
/// used to append every <c>--mount</c> after the declared entries with no guard: the console died at
/// its start on "Duplicate virtual paths", stack included, or on the registry's exception. Outside the
/// working directory, a declared entry and a <c>--commands-dir</c> are readable, a <c>--mount</c> under
/// <c>--allow-external-mounts</c>.
/// <para>
/// Serial, in the collection of <see cref="ReplSettingsSourcesTests"/>: a test sets
/// <c>ORKEON_</c> and bare variables, and the global settings path, process-wide state; each removes
/// what it set. The global file is pointed at a file of the test, absent unless the test writes it,
/// so the machine's own never takes part.
/// </para>
/// </summary>
[Collection(ReplWorkingDirectoryCollection.Name)]
public sealed class ReplMountsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("orkeon-repl-mounts-").FullName;
    private readonly string? _globalOverride = RunnerSettings.GlobalSettingsPathOverride;
    private readonly string _global;
    private readonly List<string> _reported = [];

    public ReplMountsTests()
    {
        _global = Path.Combine(_root, "global", "appsettings.json");
        RunnerSettings.GlobalSettingsPathOverride = _global;
    }

    public void Dispose()
    {
        RunnerSettings.GlobalSettingsPathOverride = _globalOverride;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string Folder(string name, string? file = null)
    {
        var path = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        if (file is not null)
            File.WriteAllText(Path.Combine(path, file), "content of " + name);
        return path;
    }

    private string Settings(string name, params string[] mounts)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            "{ \"RaggableTree\": { \"Enabled\": false }, \"Orkeon\": { \"FileSystem\": { \"Mounts\": [ "
            + string.Join(", ", mounts.Select(m => System.Text.Json.JsonSerializer.Serialize(m))) + " ] } } }");
        return path;
    }

    private ReplLaunch Start(params string[] args) =>
        ReplStartup.Prepare(args, UiMode.Plain, replWordWrap: false, _reported.Add);

    /// <summary>Starts the REPL with <paramref name="variable"/> set, removed afterwards whatever happens.</summary>
    private ReplLaunch StartWith(string? variable, string? value, params string[] args)
    {
        if (variable is null)
            return Start(args);

        Environment.SetEnvironmentVariable(variable, value);
        try
        {
            return Start(args);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    private static IReadOnlyDictionary<string, FileSystemMount> MountsByRoot(IHost host) =>
        host.Services.GetRequiredService<FileSystemRegistry>()
            .GetMounts()
            .ToDictionary(m => m.VirtualPath, StringComparer.Ordinal);

    private static bool Reads(IHost host, string virtualPath) =>
        host.Services.GetRequiredService<IFileSystemService>().ResolveAndValidate(virtualPath, FileAccessRights.Read).IsAllowed;

    [Fact]
    public void A_mount_on_a_root_the_settings_declare_replaces_that_entry_for_the_session()
    {
        var a = Folder("a");
        var b = Folder("b");
        var c = Folder("c");
        var settings = Settings("repl.json", $"{a}:/workspace:ro", $"{b}:/data:ro");
        using var logs = new CapturingLoggerProvider();

        var launch = ReplStartup.Prepare(
            ["--settings", settings, "--mount", $"{c}:/workspace:rw"],
            UiMode.Plain, replWordWrap: false, _reported.Add,
            logging => logging.AddProvider(logs));

        using var host = launch.Host;
        Assert.NotNull(host);
        Assert.Empty(_reported);
        var mounts = MountsByRoot(host);
        Assert.Equal(c, mounts["/workspace"].BasePath);
        Assert.Equal(FileAccessRights.ReadWrite, mounts["/workspace"].DefaultRights);
        Assert.Equal(b, mounts["/data"].BasePath);

        // The log says which declared entry this session overrode, in the runners' words, and
        // nothing of the root the --mount left alone.
        Assert.Contains(logs.Logger.Entries, e =>
            e.Level == LogLevel.Information && e.Message == "mount /workspace: --mount replaces the settings entry");
        Assert.DoesNotContain(logs.Logger.Entries, e => e.Message.StartsWith("mount /data", StringComparison.Ordinal));
    }

    /// <summary>Each case is refused before any host, exit 1, on the first line <c>orkeon run</c> writes for it.</summary>
    [Theory]
    [InlineData("a --mount on /sandbox")]
    [InlineData("a /credentials an ORKEON_ variable declares")]
    [InlineData("a /cli-commands a bare variable declares beside --commands-dir")]
    [InlineData("two different --mount on one root")]
    [InlineData("the same --mount twice")]
    [InlineData("a --mount to a missing folder")]
    [InlineData("a --commands-dir that is not there")]
    [InlineData("two /output of the global file and nothing to choose one")]
    public void A_mount_the_repl_cannot_honour_is_refused_before_any_host_with_the_runners_line(string scenario)
    {
        var d = Folder("d");
        var e = Folder("e");
        var missing = Path.Combine(_root, "missing");
        string? variable = null;
        string? value = null;
        string[] args;
        string expected;
        switch (scenario)
        {
            case "a --mount on /sandbox":
                args = ["--mount", $"{d}:/sandbox:rw"];
                expected = "ERROR: '/sandbox' is a virtual root reserved by the runner: this command mounts it for itself "
                    + "(reserved here: /sandbox, /credentials). Give this mount another virtual name.";
                break;
            case "a /credentials an ORKEON_ variable declares":
                (variable, value) = ("ORKEON_Orkeon__FileSystem__Mounts__0", $"{d}:/credentials:rw");
                args = [];
                expected = "ERROR: '/credentials' is a virtual root reserved by the runner: this command mounts it for itself "
                    + "(reserved here: /sandbox, /credentials). Give this mount another virtual name.";
                break;
            case "a /cli-commands a bare variable declares beside --commands-dir":
                (variable, value) = ("Orkeon__FileSystem__Mounts__0", $"{d}:/cli-commands:ro");
                args = ["--commands-dir", e];
                expected = "ERROR: '/cli-commands' is a virtual root reserved by the runner: this command mounts it for itself "
                    + "(reserved here: /sandbox, /cli-commands, /credentials). Give this mount another virtual name.";
                break;
            case "two different --mount on one root":
                args = ["--mount", $"{d}:/x:ro", "--mount", $"{e}:/x:rw"];
                expected = $"ERROR: '/x' is mounted twice on the command line: {d}:/x:ro and {e}:/x:rw. Keep one.";
                break;
            case "the same --mount twice":
                args = ["--mount", $"{d}:/x:ro", "--mount", $"{d}:/x:ro"];
                expected = $"ERROR: '/x' is mounted twice on the command line: {d}:/x:ro and {d}:/x:ro. Keep one.";
                break;
            case "a --mount to a missing folder":
                args = ["--mount", $"{missing}:/x:ro"];
                expected = $"ERROR: mount source directory does not exist: {missing}. "
                    + "Create it first (mkdir -p) or point the mount at an existing directory.";
                break;
            case "a --commands-dir that is not there":
                args = ["--commands-dir", missing];
                expected = $"ERROR: mount source directory does not exist: {missing}. "
                    + "Create it first (mkdir -p) or point the mount at an existing directory.";
                break;
            case "two /output of the global file and nothing to choose one":
                var (idA, idB) = (MountId.Create(), MountId.Create());
                Settings(Path.Combine("global", "appsettings.json"), $"{idA}|{d}:/output:rw", $"{idB}|{e}:/output:rw");
                args = [];
                expected = $"ERROR: '/output' is declared twice in {_global} ({idA}: {d}, {idB}: {e}) and nothing selects one. "
                    + "Pass --mount <folder>:/output:rw to replace them all.";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "no such scenario");
        }

        var launch = StartWith(variable, value, args);

        using var host = launch.Host;
        Assert.Null(launch.Host);
        Assert.Equal(ReplStartup.RefusedExitCode, launch.ExitCode);
        Assert.NotEmpty(_reported);
        Assert.Equal(expected, _reported[0]);
    }

    /// <summary><c>/cli-commands</c> is the REPL's only while it mounts a <c>--commands-dir</c> there.</summary>
    [Fact]
    public void A_mount_on_the_commands_root_without_a_commands_dir_is_accepted()
    {
        var d = Folder("d");

        var launch = Start("--mount", $"{d}:/cli-commands:ro");

        using var host = launch.Host;
        Assert.NotNull(host);
        Assert.Equal(d, MountsByRoot(host)["/cli-commands"].BasePath);
    }

    /// <summary>A Studio machine's global file with two <c>/output</c>: a <c>--mount</c> on the root chooses for them.</summary>
    [Fact]
    public void A_mount_on_a_root_the_global_file_declares_twice_replaces_both()
    {
        var a = Folder("a");
        var b = Folder("b");
        var c = Folder("c");
        Settings(Path.Combine("global", "appsettings.json"), $"{MountId.Create()}|{a}:/output:rw", $"{MountId.Create()}|{b}:/output:rw");

        var launch = Start("--mount", $"{c}:/output:rw");

        using var host = launch.Host;
        Assert.NotNull(host);
        Assert.Equal(c, Assert.Single(MountsByRoot(host).Values, m => m.VirtualPath == "/output").BasePath);
    }

    /// <summary>
    /// A declared entry is the machine owner's intent, readable outside the working directory without
    /// a flag, as for a runner. The REPL mounted it and refused every access to it.
    /// </summary>
    [Fact]
    public void A_declared_entry_outside_the_working_directory_is_readable()
    {
        var declared = Folder("declared", "a.txt");
        Assert.False(PhysicalPathContainment.IsUnder(declared, Directory.GetCurrentDirectory()));
        var settings = Settings("repl.json", $"{declared}:/data:ro");

        var launch = Start("--settings", settings);

        using var host = launch.Host;
        Assert.NotNull(host);
        Assert.True(Reads(host, "/data/a.txt"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("--allow-external-mounts", true)]
    [InlineData("ORKEON_ALLOW_EXTERNAL_MOUNTS", true)]
    public void A_mount_outside_the_working_directory_is_readable_under_the_opt_in_only(string? optIn, bool readable)
    {
        var external = Folder("external", "a.txt");
        string[] args = optIn == "--allow-external-mounts"
            ? ["--mount", $"{external}:/workspace:rw", optIn]
            : ["--mount", $"{external}:/workspace:rw"];
        var variable = optIn == "ORKEON_ALLOW_EXTERNAL_MOUNTS" ? RunnerEnvironment.AllowExternalMountsVariable : null;

        var launch = StartWith(variable, "1", args);

        using var host = launch.Host;
        Assert.NotNull(host);
        Assert.Equal(external, MountsByRoot(host)["/workspace"].BasePath);
        Assert.Equal(readable, Reads(host, "/workspace/a.txt"));
    }

    /// <summary>A <c>--commands-dir</c> is an entry of the REPL, like the script of <c>orkeon run</c>: read wherever it is.</summary>
    [Fact]
    public async Task A_commands_dir_outside_the_working_directory_loads_its_commands()
    {
        var commands = Folder("commands");
        await File.WriteAllTextAsync(Path.Combine(commands, "hello.cmd.ts"), """
            defineCommand({
              name: "hello",
              description: "Say hello.",
              handler: function (args, ctx) {
                return ctx.continue("hello");
              },
            });
            """, TestContext.Current.CancellationToken);

        var launch = Start("--commands-dir", commands, "--Orkeon:Cli:ScriptCommands:EsbuildTranspile=false");

        using var host = launch.Host;
        Assert.NotNull(host);
        var registry = host.Services.GetRequiredService<ScriptCommandRegistry>();
        await registry.EnsureLoadedAsync(TestContext.Current.CancellationToken);
        Assert.Contains(registry.ScriptCommands, command => command.Name == "hello");
    }
}
