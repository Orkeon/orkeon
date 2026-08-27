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

    /// <summary>
    /// The boundary holds when the internal mount's directory sits INSIDE an agent-facing
    /// one — which is the ordinary arrangement, not a corner case: <c>--llm-log ./logs</c>
    /// needs no <c>--allow-external-mounts</c> precisely because it stays under the working
    /// directory, and the working directory is what gets mounted for the agents.
    /// <para>
    /// The rest of this suite mounts the two as siblings, and passed while
    /// <c>/workspace/logs/llm-exchanges-….jsonl</c> returned the very file that
    /// <c>/llm-logs/llm-exchanges-….jsonl</c> was refused for. Refusing a name is worth
    /// nothing while the bytes keep a second address; the promise is about the bytes.
    /// </para>
    /// </summary>
    [Fact]
    public void An_internal_mount_nested_inside_an_agent_facing_one_has_no_second_address()
    {
        var workspace = SubDir("ws-nested");
        var logsDir = Path.Combine(workspace, "logs");
        Directory.CreateDirectory(logsDir);

        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(workspace, "/workspace", FileAccessRights.ReadWrite),
            new FileSystemMount(logsDir, "/llm-logs", FileAccessRights.ReadWrite, null, MountVisibility.Internal),
        ]);

        // Refused by its own name...
        Assert.Throws<FileAccessDeniedException>(() =>
            registry.ResolveAndCheckRights("/llm-logs/llm-exchanges-x.jsonl", FileAccessRights.Read));

        // ...and by the way round the parent mount.
        Assert.Throws<FileAccessDeniedException>(() =>
            registry.ResolveAndCheckRights("/workspace/logs/llm-exchanges-x.jsonl", FileAccessRights.Read));

        // The directory itself is no more addressable than a file inside it.
        Assert.Throws<FileAccessDeniedException>(() =>
            registry.ResolveAndCheckRights("/workspace/logs", FileAccessRights.Read));

        // Everything else under the workspace is untouched.
        Assert.Equal(
            Path.Combine(workspace, "notes.md"),
            registry.ResolveAndCheckRights("/workspace/notes.md", FileAccessRights.Read));

        // And the runtime itself still gets through.
        Assert.Equal(
            Path.Combine(logsDir, "llm-exchanges-x.jsonl"),
            registry.ResolveAndCheckRights("/llm-logs/llm-exchanges-x.jsonl", FileAccessRights.Write, includeInternal: true));
    }

    /// <summary>
    /// The reverse rewrite must not manufacture the address the resolve path refuses: skipping
    /// Internal mounts in the search only stops <c>ToVirtualPath</c> from answering
    /// <c>/llm-logs/x</c>, while the parent mount still matched and handed back
    /// <c>/workspace/logs/x</c>.
    /// </summary>
    [Fact]
    public void A_physical_path_inside_an_internal_mount_has_no_virtual_spelling()
    {
        var workspace = SubDir("ws-rewrite");
        var logsDir = Path.Combine(workspace, "logs");
        Directory.CreateDirectory(logsDir);

        using var registry = new FileSystemRegistry(
        [
            new FileSystemMount(workspace, "/workspace", FileAccessRights.ReadWrite),
            new FileSystemMount(logsDir, "/llm-logs", FileAccessRights.ReadWrite, null, MountVisibility.Internal),
        ]);

        Assert.Null(registry.ToVirtualPath(Path.Combine(logsDir, "llm-exchanges-x.jsonl")));
        Assert.Equal("/llm-logs/llm-exchanges-x.jsonl",
            registry.ToVirtualPath(Path.Combine(logsDir, "llm-exchanges-x.jsonl"), includeInternal: true));
        Assert.Equal("/workspace/notes.md", registry.ToVirtualPath(Path.Combine(workspace, "notes.md")));
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
