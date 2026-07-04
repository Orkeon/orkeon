using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Infrastructure.Sandbox;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Infrastructure.Tests.Sandbox;

/// <summary>
/// Unit tests for <see cref="SandboxMountBootstrapper"/>.
/// Each test uses its own isolated <see cref="EphemeralRoot"/> under <see cref="Path.GetTempPath()"/>.
/// </summary>
public sealed class SandboxMountBootstrapperTests : IDisposable
{
    private readonly string _ephemeralRoot;

    public SandboxMountBootstrapperTests()
    {
        _ephemeralRoot = Path.Combine(
            Path.GetTempPath(),
            $"orkeon-sandbox-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_ephemeralRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_ephemeralRoot))
            Directory.Delete(_ephemeralRoot, recursive: true);
    }

    // -------------------------------------------------------------------------
    // Test 1: mount is registered with Internal visibility
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProvisionsMountWithInternalVisibility()
    {
        // Arrange
        using var registry = new FileSystemRegistry(Array.Empty<FileSystemMount>());
        var bootstrapper = BuildBootstrapper(registry, applicationStopping: TestContext.Current.CancellationToken);

        // Act
        await bootstrapper.StartAsync(CancellationToken.None);

        // Assert — GetAllMountsInternal contains /sandbox
        var allMounts = registry.GetAllMountsInternal();
        Assert.Contains(allMounts, m => m.VirtualPath == "/sandbox");

        var sandboxMount = allMounts.Single(m => m.VirtualPath == "/sandbox");
        Assert.Equal(MountVisibility.Internal, sandboxMount.Visibility);
        Assert.Equal(FileAccessRights.ReadWrite, sandboxMount.DefaultRights);

        // Assert — GetAvailableMounts does NOT contain /sandbox
        var publicMounts = registry.GetAvailableMounts();
        Assert.DoesNotContain(publicMounts, m => m.VirtualPath == "/sandbox");
    }

    // -------------------------------------------------------------------------
    // Test 2: writing to /sandbox via VFS succeeds after bootstrap
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WritesToSandboxViaVfs()
    {
        // Arrange
        using var registry = new FileSystemRegistry(Array.Empty<FileSystemMount>());
        var bootstrapper = BuildBootstrapper(registry, applicationStopping: TestContext.Current.CancellationToken);
        await bootstrapper.StartAsync(CancellationToken.None);

        // Build a FileSystemService on top of the bootstrapped registry
        var pathValidator = new StubPathValidator()
            .RespondWith((_, _) => PathValidationResult.Allowed("/irrelevant"));

        var fs = new FileSystemService(
            registry,
            pathValidator,
            NullLogger<FileSystemService>.Instance);

        // Act
        var bytesWritten = await fs.WriteAllTextAsync("/sandbox/test.txt", "hi", CancellationToken.None);

        // Assert
        Assert.True(bytesWritten > 0);
        var content = await fs.TryReadAllTextAsync("/sandbox/test.txt", CancellationToken.None);
        Assert.Equal("hi", content);
    }

    // -------------------------------------------------------------------------
    // Test 3: janitor deletes orphans older than threshold
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CleansOrphansOlderThanThreshold()
    {
        // Arrange — create a fake old session directory under EphemeralRoot
        var oldSessionDir = Path.Combine(_ephemeralRoot, "99999-20200101000000");
        Directory.CreateDirectory(oldSessionDir);

        // Touch it with an old LastWriteTime (25 hours ago)
        var oldTime = DateTime.UtcNow.AddHours(-25);
        Directory.SetLastWriteTimeUtc(oldSessionDir, oldTime);

        using var registry = new FileSystemRegistry(Array.Empty<FileSystemMount>());
        var bootstrapper = BuildBootstrapper(registry, cleanupOlderThan: TimeSpan.FromHours(24), applicationStopping: TestContext.Current.CancellationToken);

        // Act
        await bootstrapper.StartAsync(CancellationToken.None);

        // Assert — the old orphan was deleted
        Assert.False(Directory.Exists(oldSessionDir),
            "Orphan directory older than threshold should have been deleted by the janitor.");
    }

    // -------------------------------------------------------------------------
    // Test 4: ApplicationStopping fires → session root is deleted
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CleansSessionOnStopping()
    {
        // Arrange — use a CancellationTokenSource so we can cancel it to simulate ApplicationStopping
        using var stoppingCts = new CancellationTokenSource();

        using var registry = new FileSystemRegistry(Array.Empty<FileSystemMount>());
        var bootstrapper = BuildBootstrapper(registry, applicationStopping: stoppingCts.Token);

        await bootstrapper.StartAsync(CancellationToken.None);

        // Capture all directories that now exist under EphemeralRoot (the session dir)
        var sessionDirs = Directory.GetDirectories(_ephemeralRoot);
        Assert.Single(sessionDirs); // exactly one session directory was created
        var sessionRoot = sessionDirs[0];

        Assert.True(Directory.Exists(sessionRoot),
            "Session root should exist right after StartAsync.");

        // Act — fire ApplicationStopping by cancelling the token
        await stoppingCts.CancelAsync();

        // Wait deterministically until the cancellation callback has deleted the session dir
        await Polling.WaitUntilAsync(() => !Directory.Exists(sessionRoot));

        // Assert
        Assert.False(Directory.Exists(sessionRoot),
            "Session root should be deleted after ApplicationStopping fires.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private SandboxMountBootstrapper BuildBootstrapper(
        FileSystemRegistry registry,
        string virtualPath = "/sandbox",
        TimeSpan? cleanupOlderThan = null,
        CancellationToken applicationStopping = default)
    {
        var opts = Options.Create(new SandboxFileSystemOptions
        {
            EphemeralRoot = _ephemeralRoot,
            VirtualPath = virtualPath,
            CleanupOrphansOlderThan = cleanupOlderThan ?? TimeSpan.FromHours(24)
        });

        var lifetime = new StubHostLifetime(applicationStopping);

        return new SandboxMountBootstrapper(
            opts,
            registry,
            lifetime,
            NullLogger<SandboxMountBootstrapper>.Instance);
    }

    private sealed class StubHostLifetime : IHostApplicationLifetime
    {
        public StubHostLifetime(CancellationToken stopping) { ApplicationStopping = stopping; }
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping { get; }
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
