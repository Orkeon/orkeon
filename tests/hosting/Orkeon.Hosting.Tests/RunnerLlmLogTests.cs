using Orkeon.Constants.FileSystem;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// <see cref="RunnerLlmLog"/> — the one rule <c>orkeon run</c> and <c>orkeon-repl</c> share for the
/// folder of the LLM exchange log: its internal mount, and the opt-in a folder outside the working
/// directory needs. The resolution of the path is covered by <see cref="RunnerOptionsBaseContractTests"/>.
/// </summary>
public sealed class RunnerLlmLogTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("orkeon-llm-log-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    [Fact]
    public void CreateMounts_ShouldCreateTheFolderAndMountItAtLlmLogs()
    {
        var logs = Path.Combine(_root, "logs");

        var mount = Assert.Single(RunnerLlmLog.CreateMounts(logs));

        Assert.True(Directory.Exists(logs));
        Assert.EndsWith($":{RunnerVirtualRoots.LlmLogs}:rw", mount, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateMounts_ShouldMountNothing_WhenLoggingIsOff()
        => Assert.Empty(RunnerLlmLog.CreateMounts(null));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void EnsureAllowed_ShouldRefuseAFolderOutsideTheWorkingDirectory_WithoutTheOptIn(bool allowExternal, bool allowed)
    {
        var cwd = Directory.CreateDirectory(Path.Combine(_root, "cwd")).FullName;
        using var error = new StringWriter();

        var result = RunnerLlmLog.EnsureAllowed(Path.Combine(_root, "elsewhere"), allowExternal, error, cwd);

        Assert.Equal(allowed, result);
        Assert.Equal(allowed, error.ToString().Length == 0);
        if (!allowed)
            Assert.Contains("--allow-external-mounts", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureAllowed_ShouldAcceptAFolderUnderTheWorkingDirectory()
    {
        var cwd = Directory.CreateDirectory(Path.Combine(_root, "cwd")).FullName;
        using var error = new StringWriter();

        Assert.True(RunnerLlmLog.EnsureAllowed(Path.Combine(cwd, "llm-logs"), allowExternalMounts: false, error, cwd));
        Assert.Empty(error.ToString());
    }
}
