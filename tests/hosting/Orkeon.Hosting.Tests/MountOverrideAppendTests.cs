using Orkeon.Constants.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// What a runner adds to the mount lists APPENDS to what <c>appsettings.json</c> declares.
/// <para>
/// The in-memory configuration source the runner writes is added last and wins on an identical
/// key, so writing <c>Orkeon:FileSystem:Mounts:0</c> did not add a mount — it replaced the
/// operator's first one. And <c>cliMounts</c> is never empty in a real run: the runner inserts
/// the crew mount at index 0 before this code sees it. So an operator who declared their data
/// mounts in a settings file lost the first of them on EVERY run, every tool touching it failed
/// "no mount found", and nothing anywhere said a mount had been dropped. <c>orkeon-host</c> lost
/// one per hosted crew directory.
/// </para>
/// <para>
/// The bug was found and fixed for the sibling key <c>PathSecurity:AdditionalAllowedDirectories</c>
/// in the same pass that left it standing here — and then copied into the brand-new
/// <c>InternalMounts</c> key. This asserts the property on all three at once, on the merged
/// registry rather than on the strings the runner produces, which is what the existing suites
/// look at and why none of them could see it.
/// </para>
/// </summary>
public sealed class MountOverrideAppendTests : IDisposable
{
    private readonly string _root;

    public MountOverrideAppendTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-mount-append-" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void A_settings_declared_mount_survives_the_mounts_the_runner_adds()
    {
        var data = SubDir("data");
        var shared = SubDir("shared");
        var crew = SubDir("crew");
        var logs = SubDir("logs");

        var settingsPath = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(settingsPath, $$"""
        {
          "Orkeon": {
            "FileSystem": {
              "Mounts": [
                {{System.Text.Json.JsonSerializer.Serialize(FileSystemMount.Quote(data) + ":/data:rw")}},
                {{System.Text.Json.JsonSerializer.Serialize(FileSystemMount.Quote(shared) + ":/shared:ro")}}
              ]
            }
          }
        }
        """);

        using var host = RunnerHost.Build(
            settingsPath,
            new RunnerMountPlan
            {
                CliMounts = [$"{FileSystemMount.Quote(crew)}:/crew:ro"],
                InternalMounts = [$"{FileSystemMount.Quote(logs)}:{RunnerVirtualRoots.LlmLogs}:rw"],
            });

        var registry = host.Services.GetRequiredService<FileSystemRegistry>();
        var virtualPaths = registry.GetAllMountsInternal().Select(m => m.VirtualPath).ToList();

        // The operator's, both of them — /data is the one that used to vanish.
        Assert.Contains("/data", virtualPaths, StringComparer.Ordinal);
        Assert.Contains("/shared", virtualPaths, StringComparer.Ordinal);

        // …alongside, not instead of, what the runner needed.
        Assert.Contains("/crew", virtualPaths, StringComparer.Ordinal);
        Assert.Contains(RunnerVirtualRoots.LlmLogs, virtualPaths, StringComparer.Ordinal);
    }

    [Fact]
    public void A_settings_declared_internal_mount_survives_too()
    {
        var vault = SubDir("vault");
        var crew = SubDir("crew2");
        var logs = SubDir("logs2");

        var settingsPath = Path.Combine(_root, "appsettings.json");
        File.WriteAllText(settingsPath, $$"""
        {
          "Orkeon": {
            "FileSystem": {
              "InternalMounts": [
                {{System.Text.Json.JsonSerializer.Serialize(FileSystemMount.Quote(vault) + ":/vault:rw")}}
              ]
            }
          }
        }
        """);

        using var host = RunnerHost.Build(
            settingsPath,
            new RunnerMountPlan
            {
                CliMounts = [$"{FileSystemMount.Quote(crew)}:/crew:ro"],
                InternalMounts = [$"{FileSystemMount.Quote(logs)}:{RunnerVirtualRoots.LlmLogs}:rw"],
            });

        var registry = host.Services.GetRequiredService<FileSystemRegistry>();
        var virtualPaths = registry.GetAllMountsInternal().Select(m => m.VirtualPath).ToList();

        Assert.Contains("/vault", virtualPaths, StringComparer.Ordinal);
        Assert.Contains(RunnerVirtualRoots.LlmLogs, virtualPaths, StringComparer.Ordinal);
    }
}
