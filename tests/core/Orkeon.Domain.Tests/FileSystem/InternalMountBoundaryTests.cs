using Orkeon.Domain.FileSystem;

namespace Orkeon.Domain.Tests.FileSystem;

/// <summary>
/// <see cref="MountVisibility.Internal"/> is a boundary, not a hiding place.
/// <para>
/// It was a hiding place. An Internal mount was absent from
/// <see cref="FileSystemRegistry.GetAvailableMounts"/> and resolved for anyone who typed its
/// name — and the names are documented: <c>/llm-logs</c> holds every prompt and every API
/// response of the run, so one <c>file_read /llm-logs/llm-exchanges-….jsonl</c> handed an
/// agent the entire exchange history. ADR-008 said the limitation out loud and left it open.
/// This suite is what closing it means.
/// </para>
/// </summary>
public sealed class InternalMountBoundaryTests : IDisposable
{
    private readonly string _tempDir;

    public InternalMountBoundaryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"orkeon-internal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private FileSystemRegistry BuildRegistry(out string logsDir)
    {
        var workspace = SubDir("ws");
        logsDir = SubDir("logs");

        return new FileSystemRegistry(
        [
            new FileSystemMount(workspace, "/workspace", FileAccessRights.ReadWrite),
            new FileSystemMount(logsDir, "/llm-logs", FileAccessRights.ReadWrite, null, MountVisibility.Internal),
        ]);
    }

    [Fact]
    public void An_internal_mount_does_not_resolve_for_an_unprivileged_caller()
    {
        using var registry = BuildRegistry(out _);

        var ex = Assert.Throws<FileAccessDeniedException>(() =>
            registry.ResolveAndCheckRights("/llm-logs/llm-exchanges-x.jsonl", FileAccessRights.Read));

        // Reported exactly like a path that does not exist: the message echoes what the
        // caller asked for and then lists the mounts, and the list must not name this one —
        // saying "it exists but is not yours" gives away what the visibility withholds.
        Assert.Contains("No mount found", ex.Message, StringComparison.Ordinal);
        var listed = ex.Message[ex.Message.IndexOf("Available mounts:", StringComparison.Ordinal)..];
        Assert.DoesNotContain("/llm-logs", listed, StringComparison.Ordinal);
        Assert.DoesNotContain("/llm-logs", ex.AlternativeMounts ?? [], StringComparer.Ordinal);
    }

    [Fact]
    public void An_internal_mount_resolves_for_a_privileged_caller()
    {
        using var registry = BuildRegistry(out var logsDir);

        var physical = registry.ResolveAndCheckRights(
            "/llm-logs/llm-exchanges-x.jsonl", FileAccessRights.Write, includeInternal: true);

        Assert.Equal(Path.Combine(logsDir, "llm-exchanges-x.jsonl"), physical);
    }

    [Fact]
    public void An_agent_facing_mount_is_unaffected()
    {
        using var registry = BuildRegistry(out _);

        Assert.NotNull(registry.ResolveAndCheckRights("/workspace/a.txt", FileAccessRights.Write));
    }

    /// <summary>
    /// A nested Internal mount is a hole punched in an agent-facing one. Falling back to the
    /// parent when the child is refused would punch the hole straight back open.
    /// </summary>
    [Fact]
    public void A_nested_internal_mount_does_not_fall_back_to_its_parent()
    {
        var workspace = SubDir("ws");
        var secret = Path.Combine(workspace, "secret");
        Directory.CreateDirectory(secret);

        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(workspace, "/workspace", FileAccessRights.ReadWrite),
            new FileSystemMount(secret, "/workspace/secret", FileAccessRights.ReadWrite, null, MountVisibility.Internal),
        ]);

        Assert.Throws<FileAccessDeniedException>(() =>
            registry.ResolveAndCheckRights("/workspace/secret/keys.json", FileAccessRights.Read));

        Assert.NotNull(registry.ResolveAndCheckRights(
            "/workspace/secret/keys.json", FileAccessRights.Read, includeInternal: true));
    }

    /// <summary>
    /// The other direction too: the shell tool rewrites physical→virtual in everything it
    /// hands back to the model, so a physical path under an Internal mount must not come back
    /// wearing that mount's name.
    /// </summary>
    [Fact]
    public void An_internal_mount_is_not_named_by_a_reverse_lookup_either()
    {
        using var registry = BuildRegistry(out var logsDir);
        var physical = Path.Combine(logsDir, "llm-exchanges-x.jsonl");

        Assert.Null(registry.ToVirtualPath(physical));
        Assert.Equal("/llm-logs/llm-exchanges-x.jsonl", registry.ToVirtualPath(physical, includeInternal: true));
    }
}
