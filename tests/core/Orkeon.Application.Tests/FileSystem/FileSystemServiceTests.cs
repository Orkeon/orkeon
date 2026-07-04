using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Application.Tests.FileSystem;

public sealed class FileSystemServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _basePath;
    private readonly FileSystemRegistry _registry;
    private readonly StubPathValidator _pathValidator;
    private readonly FileSystemService _sut;

    public FileSystemServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"orkeon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _basePath = Path.GetFullPath(_tempDir);

        var mount = new FileSystemMount(
            basePath: _basePath,
            virtualPath: "/workspace",
            defaultRights: FileAccessRights.ReadWrite);

        _registry = new FileSystemRegistry([mount]);
        _pathValidator = new StubPathValidator();

        _sut = new FileSystemService(
            _registry,
            _pathValidator,
            NullLogger<FileSystemService>.Instance);
    }

    public void Dispose()
    {
        _registry.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ResolveAndValidate_ValidPath_ChainsRegistryThenPathValidator()
    {
        // Arrange
        var expectedPhysical = Path.Combine(_basePath, "file.txt");
        _pathValidator.WhenPath(expectedPhysical, PathValidationResult.Allowed(expectedPhysical));

        // Act
        var result = _sut.ResolveAndValidate("/workspace/file.txt", FileAccessRights.Read);

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Equal(expectedPhysical, result.ResolvedPath);
        Assert.Null(result.DenialReason);
        Assert.Single(_pathValidator.Calls);
        Assert.Equal(expectedPhysical, _pathValidator.Calls[0].Path);
        Assert.Null(_pathValidator.Calls[0].WorkspaceRoot);
    }

    [Fact]
    public void ResolveAndValidate_RegistryDenies_ReturnsDenied()
    {
        // Act — "/unknown" has no matching mount
        var result = _sut.ResolveAndValidate("/unknown/file.txt", FileAccessRights.Read);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Null(result.ResolvedPath);
        Assert.NotNull(result.DenialReason);
        Assert.Contains("/unknown/file.txt", result.DenialReason);
        Assert.Empty(_pathValidator.Calls);
    }

    [Fact]
    public void ResolveAndValidate_PathValidatorDenies_ReturnsDenied()
    {
        // Arrange
        var expectedPhysical = Path.Combine(_basePath, "file.txt");
        _pathValidator.WhenPath(
            expectedPhysical,
            PathValidationResult.Denied("Path is outside the allowed workspace directory"));

        // Act
        var result = _sut.ResolveAndValidate("/workspace/file.txt", FileAccessRights.Read);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Null(result.ResolvedPath);
        Assert.NotNull(result.DenialReason);
    }

    [Fact]
    public void ResolveAndValidate_ErrorMessages_NeverContainPhysicalPaths()
    {
        // Arrange — use a mount with restricted rights to trigger denial
        var restrictedMount = new FileSystemMount(
            basePath: _basePath,
            virtualPath: "/restricted",
            defaultRights: FileAccessRights.ReadOnly);

        using var registry = new FileSystemRegistry([restrictedMount]);
        var service = new FileSystemService(
            registry,
            _pathValidator,
            NullLogger<FileSystemService>.Instance);

        // Act — Write requires Write right, but mount only has ReadOnly
        var result = service.ResolveAndValidate("/restricted/secret.txt", FileAccessRights.Write);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.NotNull(result.DenialReason);
        Assert.DoesNotContain(_basePath, result.DenialReason);
    }

    [Fact]
    public void ResolveAndValidate_PathValidatorDenial_PhysicalPathRedacted()
    {
        // Arrange — PathValidator returns a denial containing the physical path
        var expectedPhysical = Path.Combine(_basePath, "file.txt");
        _pathValidator.WhenPath(
            expectedPhysical,
            PathValidationResult.Denied($"Path '{expectedPhysical}' is outside workspace"));

        // Act
        var result = _sut.ResolveAndValidate("/workspace/file.txt", FileAccessRights.Read);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.NotNull(result.DenialReason);
        Assert.DoesNotContain(_basePath, result.DenialReason);
        Assert.Contains("[REDACTED]", result.DenialReason);
    }

    [Fact]
    public void ToVirtualPath_DelegatesToRegistry()
    {
        // Arrange
        var physicalPath = Path.Combine(_basePath, "sub", "file.txt");

        // Act
        var virtualPath = _sut.ToVirtualPath(physicalPath);

        // Assert
        Assert.NotNull(virtualPath);
        Assert.StartsWith("/workspace/", virtualPath);
    }

    [Fact]
    public void GetAvailableMounts_DelegatesWithoutPhysicalPaths()
    {
        // Act
        var mounts = _sut.GetAvailableMounts();

        // Assert
        Assert.Single(mounts);
        Assert.Equal("/workspace", mounts[0].VirtualPath);
        Assert.Equal(FileAccessRights.ReadWrite, mounts[0].DefaultRights);
    }
}
