using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Domain.Tests.FileSystem;

/// <summary>
/// Unit tests for the ambient per-execution VFS mount scope (P2-O-05): entering, restoring on
/// dispose, and nesting. Isolation across concurrent async flows is provided by
/// <see cref="System.Threading.AsyncLocal{T}"/> and covered at the service level.
/// </summary>
public sealed class AsyncLocalFileSystemScopeTests
{
    private static FileSystemRegistry Registry(string virtualPath)
        => new([new FileSystemMount("/tmp/base", virtualPath, FileAccessRights.ReadOnly)]);

    [Fact]
    public void Current_ShouldBeNull_WhenNothingEntered()
    {
        var scope = new AsyncLocalFileSystemScope();
        Assert.Null(scope.Current);
    }

    [Fact]
    public void Enter_ShouldSetCurrent_AndRestoreOnDispose()
    {
        var scope = new AsyncLocalFileSystemScope();
        using var registry = Registry("/scoped");

        Assert.Null(scope.Current);
        using (scope.Enter(registry))
        {
            Assert.Same(registry, scope.Current);
        }
        Assert.Null(scope.Current);
    }

    [Fact]
    public void Enter_ShouldRestorePreviousRegistry_WhenNested()
    {
        var scope = new AsyncLocalFileSystemScope();
        using var outer = Registry("/outer");
        using var inner = Registry("/inner");

        using (scope.Enter(outer))
        {
            Assert.Same(outer, scope.Current);
            using (scope.Enter(inner))
            {
                Assert.Same(inner, scope.Current);
            }
            Assert.Same(outer, scope.Current); // inner disposed → outer restored
        }
        Assert.Null(scope.Current);
    }

    [Fact]
    public void Enter_ShouldThrow_WhenRegistryNull()
    {
        var scope = new AsyncLocalFileSystemScope();
        Assert.Throws<ArgumentNullException>(() => scope.Enter(null!));
    }

    [Fact]
    public async System.Threading.Tasks.Task Current_ShouldBeIsolated_AcrossConcurrentAsyncFlows()
    {
        var scope = new AsyncLocalFileSystemScope();

        async System.Threading.Tasks.Task<string?> RunWith(string virtualPath)
        {
            using var registry = Registry(virtualPath);
            using (scope.Enter(registry))
            {
                await System.Threading.Tasks.Task.Yield();
                // Each flow must observe only its own entered registry.
                var mounts = scope.Current?.GetAvailableMounts();
                return mounts is { Count: > 0 } ? mounts[0].VirtualPath : null;
            }
        }

        var a = RunWith("/a");
        var b = RunWith("/b");
        var results = await System.Threading.Tasks.Task.WhenAll(a, b);

        Assert.Equal("/a", results[0]);
        Assert.Equal("/b", results[1]);
        Assert.Null(scope.Current); // ambient value did not leak into the test flow
    }
}
