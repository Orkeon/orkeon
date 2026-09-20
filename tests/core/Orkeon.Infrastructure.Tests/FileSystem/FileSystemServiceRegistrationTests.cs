using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.FileSystem;

public sealed class FileSystemServiceRegistrationTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemServiceRegistrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"orkeon-reg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void AddOrkeonFileSystem_NoMounts_ThrowsInvalidOperation()
    {
        // Arrange
        var config = BuildConfiguration([]); // empty mounts
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPathValidator>(new StubPathValidator().AllowAll());
        services.AddOrkeonFileSystem(config);

        var provider = services.BuildServiceProvider();

        // Act & Assert — resolving FileSystemRegistry triggers the factory
        var ex = Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<FileSystemRegistry>());

        Assert.Contains("at least one", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddOrkeonFileSystem_MissingBasePath_ThrowsDirectoryNotFound()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}");
        var mountStr = $"{nonExistentPath}:/data:rw";

        var config = BuildConfiguration([mountStr]);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPathValidator>(new StubPathValidator().AllowAll());
        services.AddOrkeonFileSystem(config);

        var provider = services.BuildServiceProvider();

        // Act & Assert
        var ex = Assert.Throws<DirectoryNotFoundException>(() =>
            provider.GetRequiredService<FileSystemRegistry>());

        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void AddOrkeonFileSystem_ValidConfig_RegistersServices()
    {
        // Arrange
        var mountStr = $"{_tempDir}:/workspace:rw";
        var config = BuildConfiguration([mountStr]);

        var pathValidator = new StubPathValidator()
            .RespondWith((_, _) => PathValidationResult.Allowed("/some/path"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPathValidator>(pathValidator);
        services.AddOrkeonFileSystem(config);

        var provider = services.BuildServiceProvider();

        // Act
        var registry = provider.GetRequiredService<FileSystemRegistry>();
        var fileSystemService = provider.GetRequiredService<IFileSystemService>();

        // Assert
        Assert.NotNull(registry);
        Assert.NotNull(fileSystemService);
        Assert.IsType<FileSystemService>(fileSystemService);

        var mounts = fileSystemService.GetAvailableMounts();
        Assert.Single(mounts);
        Assert.Equal("/workspace", mounts[0].VirtualPath);
    }

    [Fact]
    public void AddOrkeonFileSystem_BlankAndNullEntries_AreSkipped()
    {
        // VFS-90: an index the host vacated for the run (another entry of the same root was
        // selected, or a --mount replaced them all) is written empty and must not be parsed.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:FileSystem:Mounts:0"] = "",
                ["Orkeon:FileSystem:Mounts:1"] = $"{_tempDir}:/workspace:rw",
                ["Orkeon:FileSystem:Mounts:2"] = null,
                ["Orkeon:FileSystem:Mounts:3"] = "   ",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPathValidator>(new StubPathValidator().AllowAll());
        services.AddOrkeonFileSystem(config);

        var mounts = services.BuildServiceProvider().GetRequiredService<FileSystemRegistry>().GetAvailableMounts();

        Assert.Single(mounts);
        Assert.Equal("/workspace", mounts[0].VirtualPath);
    }

    [Fact]
    public void AddOrkeonFileSystem_IdPrefixedEntry_KeepsTheIdOnTheAgentFacingMountOnly()
    {
        const string id = "01J9Z3K4M5N6P7Q8R9S0T1V2W3";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:FileSystem:Mounts:0"] = $"{id}|{_tempDir}:/workspace:rw",
                ["Orkeon:FileSystem:InternalMounts:0"] = $"01J9Z3K4M5N6P7Q8R9S0T1V2W4|{_tempDir}:/llm-logs:rw",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPathValidator>(new StubPathValidator().AllowAll());
        services.AddOrkeonFileSystem(config);

        var all = services.BuildServiceProvider().GetRequiredService<FileSystemRegistry>().GetAllMountsInternal();

        Assert.Equal(id, all.Single(m => m.VirtualPath == "/workspace").Id!.ToString());
        Assert.Null(all.Single(m => m.VirtualPath == "/llm-logs").Id);
    }

    [Fact]
    public void AddOrkeonFileSystem_ShouldRegisterFileSystemScope_AsSingleton()
    {
        // P2-O-05: the ambient mount scope must be a singleton so the host that enters a scoped
        // registry and the singleton FileSystemService that reads it share the same AsyncLocal slot.
        var config = BuildConfiguration([$"{_tempDir}:/workspace:rw"]);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IPathValidator>(new StubPathValidator().AllowAll());
        services.AddOrkeonFileSystem(config);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IFileSystemScope));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(AsyncLocalFileSystemScope), descriptor.ImplementationType);

        using var provider = services.BuildServiceProvider();
        // With no scope entered, the service exposes the boot mount unchanged.
        var scope = provider.GetRequiredService<IFileSystemScope>();
        Assert.Null(scope.Current);
        Assert.Single(provider.GetRequiredService<IFileSystemService>().GetAvailableMounts());
    }

    private static IConfiguration BuildConfiguration(string[] mounts)
    {
        var data = new Dictionary<string, string?>();
        for (var i = 0; i < mounts.Length; i++)
        {
            data[$"Orkeon:FileSystem:Mounts:{i}"] = mounts[i];
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
    }
}
