using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Infrastructure.Sandbox;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.Sandbox;

/// <summary>
/// Unit tests for <see cref="SandboxSession"/> — the per-process sandbox directory and its
/// Internal <c>/sandbox</c> mount. Each test uses its own isolated ephemeral root under
/// <see cref="Path.GetTempPath()"/>.
/// <para>
/// The last test is the one that matters: it asks a container wired the way a real host wires
/// it whether <c>/sandbox</c> resolves. The predecessor of this class proved the same mount at
/// the level of the object that built it, and that object was a hosted service nothing started
/// outside the daemon — so every assertion passed while the shipped CLIs registered the whole
/// code-execution subsystem over a virtual root that did not exist.
/// </para>
/// </summary>
public sealed class SandboxSessionTests : IDisposable
{
    private readonly string _ephemeralRoot;

    public SandboxSessionTests()
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

    [Fact]
    public void ProvisionsMountWithInternalVisibility()
    {
        using var session = BuildSession();
        using var registry = new FileSystemRegistry([session.Mount]);

        var sandboxMount = Assert.Single(registry.GetAllMountsInternal(), m => m.VirtualPath == "/sandbox");
        Assert.Equal(MountVisibility.Internal, sandboxMount.Visibility);
        Assert.Equal(FileAccessRights.ReadWrite, sandboxMount.DefaultRights);

        Assert.DoesNotContain(registry.GetAvailableMounts(), m => m.VirtualPath == "/sandbox");
    }

    [Fact]
    public async Task WritesToSandboxViaVfs()
    {
        using var session = BuildSession();
        using var registry = new FileSystemRegistry([session.Mount]);

        var pathValidator = new StubPathValidator()
            .RespondWith((_, _) => PathValidationResult.Allowed("/irrelevant"));

        var fs = new FileSystemService(
            registry,
            pathValidator,
            NullLogger<FileSystemService>.Instance);

        var bytesWritten = await fs.WriteAllTextAsync("/sandbox/test.txt", "hi", TestContext.Current.CancellationToken);

        Assert.True(bytesWritten > 0);
        Assert.Equal("hi", await fs.TryReadAllTextAsync("/sandbox/test.txt", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CleansOrphansOlderThanThreshold()
    {
        var oldSessionDir = Path.Combine(_ephemeralRoot, "99999-20200101000000");
        Directory.CreateDirectory(oldSessionDir);
        Directory.SetLastWriteTimeUtc(oldSessionDir, DateTime.UtcNow.AddHours(-25));

        using var session = BuildSession(cleanupOlderThan: TimeSpan.FromHours(24));

        Assert.False(
            Directory.Exists(oldSessionDir),
            "Orphan directory older than threshold should have been deleted by the janitor.");
    }

    [Fact]
    public void KeepsOrphansYoungerThanThreshold()
    {
        var recentSessionDir = Path.Combine(_ephemeralRoot, "99998-20990101000000");
        Directory.CreateDirectory(recentSessionDir);

        using var session = BuildSession(cleanupOlderThan: TimeSpan.FromHours(24));

        Assert.True(
            Directory.Exists(recentSessionDir),
            "A concurrent process's live sandbox must survive another process's janitor sweep.");
    }

    [Fact]
    public void CleansItsSessionDirectoryOnDispose()
    {
        var session = BuildSession();
        var root = session.Root;
        Assert.True(Directory.Exists(root));

        session.Dispose();

        Assert.False(Directory.Exists(root), "The session directory is deleted when the host is disposed.");
    }

    /// <summary>
    /// The promise, asked at the level it is made: a container wired by
    /// <see cref="FileSystemServiceRegistration.AddOrkeonFileSystem"/> resolves <c>/sandbox</c>
    /// — no hosted service started, exactly as the runners build it.
    /// </summary>
    [Fact]
    public void AddOrkeonFileSystem_MountsTheSandbox_WithoutStartingAnyHostedService()
    {
        var workspace = Path.Combine(_ephemeralRoot, "ws");
        Directory.CreateDirectory(workspace);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:FileSystem:Mounts:0"] = FileSystemMount.Quote(workspace) + ":/workspace:rw",
                ["Orkeon:Sandbox:EphemeralRoot"] = _ephemeralRoot,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOrkeonFileSystem(configuration);
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<FileSystemRegistry>();

        Assert.Contains(registry.GetAllMountsInternal(), m => m.VirtualPath == "/sandbox");
        Assert.DoesNotContain(registry.GetAvailableMounts(), m => m.VirtualPath == "/sandbox");

        // The code sandboxes write under /sandbox/docker and /sandbox/process: the root has to
        // resolve, not merely be listed.
        var physical = registry.ResolveAndCheckRights("/sandbox/docker/run-1", FileAccessRights.Write);
        Assert.StartsWith(_ephemeralRoot, physical, StringComparison.Ordinal);
    }

    private SandboxSession BuildSession(string virtualPath = "/sandbox", TimeSpan? cleanupOlderThan = null) =>
        new(
            Options.Create(new SandboxFileSystemOptions
            {
                EphemeralRoot = _ephemeralRoot,
                VirtualPath = virtualPath,
                CleanupOrphansOlderThan = cleanupOlderThan ?? TimeSpan.FromHours(24),
            }),
            NullLogger<SandboxSession>.Instance);
}
