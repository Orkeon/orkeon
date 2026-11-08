using Microsoft.Extensions.DependencyInjection;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.FileSystem;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// The rules of the cassette's folder (<c>--llm-record</c>, <c>--llm-replay</c>): recording writes, so a
/// path outside the working directory needs the external-mounts opt-in; replaying never writes, so its
/// mount is read-only. Each rule is held at the guard and, for the mount, through a real host.
/// </summary>
public sealed class RunnerLlmCassetteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-hosting-llm-cassette-rules-" + Guid.NewGuid().ToString("N"));

    public RunnerLlmCassetteTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    private static (bool Allowed, string Error) Write(string? record, bool allowExternal)
    {
        using var error = new StringWriter();
        var allowed = RunnerLlmCassette.EnsureWriteAllowed(
            RunnerLlmCassette.Resolve(record, null), allowExternal, error, Directory.GetCurrentDirectory());
        return (allowed, error.ToString());
    }

    [Fact]
    public void A_relative_path_that_escapes_the_working_directory_is_refused_without_the_opt_in()
    {
        var (allowed, error) = Write(Path.Combine("..", "..", "x", "run.jsonl"), allowExternal: false);

        Assert.False(allowed);
        Assert.Contains("--llm-record", error, StringComparison.Ordinal);
        Assert.Contains("--allow-external-mounts", error, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absolute_path_outside_the_working_directory_is_refused_without_the_opt_in()
    {
        var (allowed, error) = Write(Path.Combine(_root, "run.jsonl"), allowExternal: false);

        Assert.False(allowed);
        Assert.Contains("--llm-record", error, StringComparison.Ordinal);
        Assert.Contains(_root, error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_outside_the_working_directory_is_accepted_with_the_opt_in()
    {
        var (absolute, absoluteError) = Write(Path.Combine(_root, "run.jsonl"), allowExternal: true);
        var (relative, relativeError) = Write(Path.Combine("..", "..", "x", "run.jsonl"), allowExternal: true);

        Assert.True(absolute, absoluteError);
        Assert.True(relative, relativeError);
    }

    [Fact]
    public void A_path_under_the_working_directory_needs_no_opt_in()
    {
        var (allowed, error) = Write(Path.Combine("cassettes", "run.jsonl"), allowExternal: false);

        Assert.True(allowed, error);
    }

    [Fact]
    public void A_replay_writes_nothing_so_it_never_needs_the_opt_in()
    {
        using var error = new StringWriter();

        var allowed = RunnerLlmCassette.EnsureWriteAllowed(
            RunnerLlmCassette.Resolve(null, Path.Combine(_root, "run.jsonl")), allowExternalMounts: false, error, Directory.GetCurrentDirectory());

        Assert.True(allowed, error.ToString());
    }

    private static RunnerMountPlan Plan(string cassette, bool records)
    {
        var choice = RunnerLlmCassette.Resolve(records ? cassette : null, records ? null : cassette);
        return new RunnerMountPlan
        {
            InternalMounts = RunnerLlmCassette.CreateMounts(choice),
            InputDirectories = RunnerLlmCassette.InputDirectories(choice),
            AllowExternalMounts = true,
            LlmRecordCassette = records ? RunnerLlmCassette.VirtualPath(choice) : null,
            LlmReplayCassette = records ? null : RunnerLlmCassette.VirtualPath(choice),
        };
    }

    [Fact]
    public async Task A_replay_cannot_write_under_the_cassette_folder_and_the_cassette_is_unchanged()
    {
        var cassette = Path.Combine(_root, "run.jsonl");
        await File.WriteAllTextAsync(cassette, "{\"v\":1}\n", TestContext.Current.CancellationToken);
        using var host = RunnerHost.Build(null, Plan(cassette, records: false));
        var fileSystem = host.Services.GetRequiredService<PrivilegedFileSystemAccess>().FileSystem;

        var refusal = await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            fileSystem.WriteAllTextAsync(RunnerVirtualRoots.LlmCassette + "/run.jsonl", "tampered", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            fileSystem.AppendAllTextAsync(RunnerVirtualRoots.LlmCassette + "/extra.jsonl", "x", TestContext.Current.CancellationToken));

        Assert.Contains("/llm-cassette/run.jsonl", refusal.Message, StringComparison.Ordinal);
        Assert.Equal("{\"v\":1}\n", await File.ReadAllTextAsync(cassette, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(_root, "extra.jsonl")));
    }

    [Fact]
    public async Task A_record_can_write_under_the_cassette_folder()
    {
        var cassette = Path.Combine(_root, "recorded", "run.jsonl");
        using var host = RunnerHost.Build(null, Plan(cassette, records: true));
        var fileSystem = host.Services.GetRequiredService<PrivilegedFileSystemAccess>().FileSystem;

        await fileSystem.WriteAllTextAsync(RunnerVirtualRoots.LlmCassette + "/run.jsonl", "line", TestContext.Current.CancellationToken);

        Assert.Equal("line", await File.ReadAllTextAsync(cassette, TestContext.Current.CancellationToken));
    }
}
