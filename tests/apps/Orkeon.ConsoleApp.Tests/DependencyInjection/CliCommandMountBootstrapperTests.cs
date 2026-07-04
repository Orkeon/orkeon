using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Cli.Scripting.Configuration;
using Orkeon.ConsoleApp.DependencyInjection;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>
/// Verifies plan Q7 = option (a): <c>--commands-dir</c> paths are bootstrapped through
/// <c>PostConfigure</c> on <see cref="FileSystemOptions"/> and
/// <see cref="ScriptCommandsConfiguration"/> rather than via an in-memory configuration
/// overlay.
/// </summary>
public sealed class CliCommandMountBootstrapperTests : IDisposable
{
    private static readonly string[] ExpectedScriptDirectories = ["/cli-commands", "/cli-commands-1"];

    private readonly string _tempA;
    private readonly string _tempB;

    public CliCommandMountBootstrapperTests()
    {
        _tempA = Path.Combine(Path.GetTempPath(), "orkeon-mount-a-" + Guid.NewGuid().ToString("N"));
        _tempB = Path.Combine(Path.GetTempPath(), "orkeon-mount-b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempA);
        Directory.CreateDirectory(_tempB);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempA, true); } catch { /* best effort */ }
        try { Directory.Delete(_tempB, true); } catch { /* best effort */ }
    }

    [Fact]
    public void Empty_list_is_a_no_op()
    {
        var sc = new ServiceCollection();
        sc.AddOptions<FileSystemOptions>();
        sc.AddOptions<ScriptCommandsConfiguration>();
        sc.AddScriptCommandMounts(Array.Empty<string>());
        using var sp = sc.BuildServiceProvider();
        Assert.Empty(sp.GetRequiredService<IOptions<FileSystemOptions>>().Value.Mounts);
        Assert.True(sp.GetRequiredService<IOptions<ScriptCommandsConfiguration>>().Value.Directories.IsDefaultOrEmpty);
    }

    [Fact]
    public void First_path_mounts_at_cli_commands_root()
    {
        var sc = new ServiceCollection();
        sc.AddOptions<FileSystemOptions>();
        sc.AddOptions<ScriptCommandsConfiguration>();
        sc.AddScriptCommandMounts(new[] { _tempA });
        using var sp = sc.BuildServiceProvider();

        var fsOpts = sp.GetRequiredService<IOptions<FileSystemOptions>>().Value;
        var scriptOpts = sp.GetRequiredService<IOptions<ScriptCommandsConfiguration>>().Value;

        Assert.Single(fsOpts.Mounts);
        Assert.Equal($"{Path.GetFullPath(_tempA)}:/cli-commands:ro", fsOpts.Mounts[0]);
        Assert.Single(scriptOpts.Directories);
        Assert.Equal("/cli-commands", scriptOpts.Directories[0]);
    }

    [Fact]
    public void Subsequent_paths_get_indexed_virtual_root()
    {
        var sc = new ServiceCollection();
        sc.AddOptions<FileSystemOptions>();
        sc.AddOptions<ScriptCommandsConfiguration>();
        sc.AddScriptCommandMounts(new[] { _tempA, _tempB });
        using var sp = sc.BuildServiceProvider();

        var fsOpts = sp.GetRequiredService<IOptions<FileSystemOptions>>().Value;
        var scriptOpts = sp.GetRequiredService<IOptions<ScriptCommandsConfiguration>>().Value;

        Assert.Equal(2, fsOpts.Mounts.Count);
        Assert.Contains($"{Path.GetFullPath(_tempA)}:/cli-commands:ro", fsOpts.Mounts);
        Assert.Contains($"{Path.GetFullPath(_tempB)}:/cli-commands-1:ro", fsOpts.Mounts);
        Assert.Equal(ExpectedScriptDirectories, scriptOpts.Directories);
    }

    [Fact]
    public void Preserves_appsettings_mounts_and_directories()
    {
        var sc = new ServiceCollection();
        // Simulate appsettings-bound values.
        sc.Configure<FileSystemOptions>(o => o.Mounts.Add("/var/data:/data:rw"));
        sc.Configure<ScriptCommandsConfiguration>(o =>
            o.Directories = System.Collections.Immutable.ImmutableArray.Create("/preexisting"));

        sc.AddScriptCommandMounts(new[] { _tempA });
        using var sp = sc.BuildServiceProvider();

        var fsOpts = sp.GetRequiredService<IOptions<FileSystemOptions>>().Value;
        var scriptOpts = sp.GetRequiredService<IOptions<ScriptCommandsConfiguration>>().Value;

        Assert.Contains("/var/data:/data:rw", fsOpts.Mounts);
        Assert.Contains($"{Path.GetFullPath(_tempA)}:/cli-commands:ro", fsOpts.Mounts);
        Assert.Contains("/preexisting", scriptOpts.Directories);
        Assert.Contains("/cli-commands", scriptOpts.Directories);
    }

    [Fact]
    public void Idempotent_when_called_twice_with_same_paths()
    {
        var sc = new ServiceCollection();
        sc.AddOptions<FileSystemOptions>();
        sc.AddOptions<ScriptCommandsConfiguration>();
        sc.AddScriptCommandMounts(new[] { _tempA });
        sc.AddScriptCommandMounts(new[] { _tempA });
        using var sp = sc.BuildServiceProvider();

        var fsOpts = sp.GetRequiredService<IOptions<FileSystemOptions>>().Value;
        Assert.Single(fsOpts.Mounts);
    }
}
