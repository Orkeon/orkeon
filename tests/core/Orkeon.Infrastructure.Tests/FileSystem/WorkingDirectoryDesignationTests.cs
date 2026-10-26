using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Constants.Configuration;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.FileSystem;

public sealed class WorkingDirectoryDesignationTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("orkeon-wd-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string Dir(string name) => Directory.CreateDirectory(Path.Combine(_tempDir, name)).FullName;

    [Fact]
    [Trait("BR", "CH-199")]
    public void ShouldMatchTheRunnerWorkspaceRoot_WhenTheDomainNamesItsDefault()
    {
        Assert.Equal(RunnerVirtualRoots.Workspace, FileSystemRegistry.DefaultWorkingDirectory);
    }

    [Fact]
    [Trait("BR", "CH-199")]
    public void ShouldKeepTheWorkspace_WhenItIsMounted()
    {
        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(Dir("history"), "/session-history", FileAccessRights.ReadOnly),
            new FileSystemMount(Dir("ws"), "/workspace", FileAccessRights.ReadWrite),
            new FileSystemMount(Dir("out"), "/output", FileAccessRights.ReadWrite),
        ]);

        WorkingDirectoryDesignation.Apply(registry, configured: null);

        Assert.Equal("/workspace", registry.WorkingDirectory);
    }

    [Fact]
    [Trait("BR", "CH-199")]
    public void ShouldDesignateOutput_WhenNoWorkspaceIsMounted()
    {
        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(Dir("history"), "/session-history", FileAccessRights.ReadOnly),
            new FileSystemMount(Dir("out"), "/output", FileAccessRights.ReadWrite),
        ]);

        WorkingDirectoryDesignation.Apply(registry, configured: null);

        Assert.Equal("/output", registry.WorkingDirectory);
    }

    [Fact]
    [Trait("BR", "CH-199")]
    public void ShouldLeaveNone_WhenNeitherWorkspaceNorOutputIsMounted()
    {
        using var registry = new FileSystemRegistry([new FileSystemMount(Dir("data"), "/data", FileAccessRights.ReadWrite)]);

        WorkingDirectoryDesignation.Apply(registry, configured: null);

        Assert.Null(registry.WorkingDirectory);
    }

    [Fact]
    [Trait("BR", "CH-199")]
    public void ShouldNameTheSetting_WhenTheConfiguredDirectoryIsInNoMount()
    {
        using var registry = new FileSystemRegistry([new FileSystemMount(Dir("data"), "/data", FileAccessRights.ReadWrite)]);

        var ex = Assert.Throws<InvalidOperationException>(() => WorkingDirectoryDesignation.Apply(registry, "/nowhere"));

        Assert.Contains(ConfigurationKeys.FileSystemWorkingDirectory, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-199")]
    public void ShouldHonourTheSetting_WhenTheRegistryIsBuiltFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ConfigurationKeys.FileSystemMounts}:0"] = $"{Dir("ws")}:/workspace:rw",
                [$"{ConfigurationKeys.FileSystemMounts}:1"] = $"{Dir("data")}:/data:rw",
                [ConfigurationKeys.FileSystemWorkingDirectory] = "/data/src",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPathValidator>(new StubPathValidator().AllowAll());
        services.AddOrkeonFileSystem(config);
        using var provider = services.BuildServiceProvider();

        var fileSystem = provider.GetRequiredService<IFileSystemService>();

        Assert.Equal("/data/src", fileSystem.WorkingDirectory);
    }

    [Fact]
    [Trait("BR", "CH-199")]
    public void ShouldCarryTheBootWorkingDirectory_WhenTheExecutionStillMountsIt()
    {
        using var boot = new FileSystemRegistry(
        [
            new FileSystemMount(Dir("data"), "/data", FileAccessRights.ReadWrite),
            new FileSystemMount(Dir("ws"), "/workspace", FileAccessRights.ReadWrite),
        ]);
        boot.SetWorkingDirectory("/data");

        using var execution = ScopedMountComposition.ForExecution(
            boot, [new FileSystemMount(Dir("out"), "/output", FileAccessRights.ReadWrite)]);

        Assert.Equal("/data", execution.WorkingDirectory);
    }

    [Fact]
    [Trait("BR", "CH-199")]
    public void ShouldDesignateTheExecutionsOwnOutput_WhenTheBootHasNoWorkingDirectory()
    {
        using var boot = new FileSystemRegistry([new FileSystemMount(Dir("crews"), "/crews", FileAccessRights.ReadOnly)]);

        using var execution = ScopedMountComposition.ForExecution(
            boot, [new FileSystemMount(Dir("out"), "/output", FileAccessRights.ReadWrite)]);

        Assert.Equal("/output", execution.WorkingDirectory);
    }

    [Fact]
    [Trait("BR", "CH-199")]
    public void ShouldTakeOutputAsWriteDirectory_WhenItIsWritable()
    {
        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(Dir("ws"), "/workspace", FileAccessRights.ReadOnly),
            new FileSystemMount(Dir("out"), "/output", FileAccessRights.ReadWrite),
        ]);

        WorkingDirectoryDesignation.Apply(registry, configured: null);

        Assert.Equal(RunnerVirtualRoots.Output, FileSystemRegistry.DefaultWriteDirectory);
        Assert.Equal("/workspace", registry.WorkingDirectory);
        Assert.Equal("/output", registry.WriteDirectory);
    }

    [Fact]
    [Trait("BR", "CH-199")]
    public void ShouldLeaveNoWriteDirectory_WhenOutputIsReadOnlyEvenIfAnotherMountIsWritable()
    {
        // Explicit, never the order of the mounts: a writable /data is not taken by itself.
        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(Dir("data"), "/data", FileAccessRights.ReadWrite),
            new FileSystemMount(Dir("out"), "/output", FileAccessRights.ReadOnly),
        ]);

        WorkingDirectoryDesignation.Apply(registry, configured: null);

        Assert.Null(registry.WriteDirectory);
    }

    [Fact]
    [Trait("BR", "CH-199")]
    public void ShouldNameTheSetting_WhenTheConfiguredWriteDirectoryIsReadOnly()
    {
        using var registry = new FileSystemRegistry([new FileSystemMount(Dir("docs"), "/docs", FileAccessRights.ReadOnly)]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => WorkingDirectoryDesignation.Apply(registry, configured: null, configuredWrite: "/docs"));

        Assert.Contains(ConfigurationKeys.FileSystemWriteDirectory, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-199")]
    [InlineData("/output/../workspace")]
    [InlineData("/output/ro")]
    public void ShouldNameTheSetting_WhenTheConfiguredWriteDirectoryTraversesOrIsMadeReadOnly(string configuredWrite)
    {
        // Both used to be accepted at start, then refused at every writing command.
        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(Dir("ws"), "/workspace", FileAccessRights.ReadWrite),
            new FileSystemMount(Dir("out"), "/output", FileAccessRights.ReadWrite,
                [new SubPathOverride("ro", FileAccessRights.ReadOnly)]),
        ]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => WorkingDirectoryDesignation.Apply(registry, configured: null, configuredWrite: configuredWrite));

        Assert.Contains(ConfigurationKeys.FileSystemWriteDirectory, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-199")]
    public void ShouldHonourTheWriteDirectorySetting_WhenTheRegistryIsBuiltFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ConfigurationKeys.FileSystemMounts}:0"] = $"{Dir("ws")}:/workspace:ro",
                [$"{ConfigurationKeys.FileSystemMounts}:1"] = $"{Dir("scratch")}:/scratch:rw",
                [ConfigurationKeys.FileSystemWriteDirectory] = "/scratch",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPathValidator>(new StubPathValidator().AllowAll());
        services.AddOrkeonFileSystem(config);
        using var provider = services.BuildServiceProvider();

        var fileSystem = provider.GetRequiredService<IFileSystemService>();

        Assert.Equal("/workspace", fileSystem.WorkingDirectory);
        Assert.Equal("/scratch", fileSystem.WriteDirectory);
    }

    [Fact]
    [Trait("BR", "CH-199")]
    public void ShouldCarryTheBootWriteDirectory_WhenTheExecutionStillMountsIt()
    {
        using var boot = new FileSystemRegistry(
        [
            new FileSystemMount(Dir("scratch"), "/scratch", FileAccessRights.ReadWrite),
            new FileSystemMount(Dir("ws"), "/workspace", FileAccessRights.ReadOnly),
        ]);
        boot.SetWriteDirectory("/scratch");

        using var execution = ScopedMountComposition.ForExecution(
            boot, [new FileSystemMount(Dir("crew"), "/crew", FileAccessRights.ReadOnly)]);

        Assert.Equal("/scratch", execution.WriteDirectory);
    }
}
