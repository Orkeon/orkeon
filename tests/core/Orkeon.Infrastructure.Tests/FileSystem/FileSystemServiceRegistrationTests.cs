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
