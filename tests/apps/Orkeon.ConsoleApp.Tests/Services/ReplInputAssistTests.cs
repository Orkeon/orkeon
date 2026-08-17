using Orkeon.Cli.Commands;
using Orkeon.Cli.Registry;
using Orkeon.Cli.Commands.Scripting.Registry;
using Orkeon.Cli.Commands.Scripting.Runtime;
using Orkeon.ConsoleApp.Services;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.ConsoleApp.Tests.Services;

/// <summary>
/// Covers <see cref="ReplInputAssist"/>'s completion over a real disk-backed VFS. Tests may use
/// real disk via <see cref="DiskBackedFileSystemService"/> (CLAUDE.md VFS exception for tests).
/// </summary>
public sealed class ReplInputAssistTests : IDisposable
{
    private readonly string _tempDir;

    public ReplInputAssistTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-replassist-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "src"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "docs"));
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "x");
        File.WriteAllText(Path.Combine(_tempDir, "src", "Program.cs"), "x");
    }

    private ReplInputAssist NewAssist()
    {
        var fs = new DiskBackedFileSystemService(_tempDir, virtualRoot: "/workspace");
        var scripts = new ScriptCommandRegistry(Array.Empty<ScriptCommand>());
        var defaults = new DefaultCommandRegistry(new HelpCommand(), new ExitCommand(), new ClearCommand());
        return new ReplInputAssist(scripts, defaults, fs);
    }

    [Fact]
    public void CompletePath_lists_files_and_directories_at_the_root()
    {
        var results = NewAssist().CompletePath("");

        Assert.Contains("README.md", results);
        Assert.Contains("src/", results);   // directory → trailing slash
        Assert.Contains("docs/", results);  // directory → trailing slash
    }

    [Fact]
    public void CompletePath_completes_a_directory_by_prefix()
    {
        var results = NewAssist().CompletePath("sr");

        Assert.Contains("src/", results);
        Assert.DoesNotContain("README.md", results);
    }

    [Fact]
    public void CompletePath_descends_into_a_completed_directory()
    {
        var results = NewAssist().CompletePath("src/");

        Assert.Contains("src/Program.cs", results);
    }

    [Fact]
    public void CompleteCommand_matches_default_commands()
    {
        var results = NewAssist().CompleteCommand("ex");

        Assert.Contains("exit", results);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }
}
