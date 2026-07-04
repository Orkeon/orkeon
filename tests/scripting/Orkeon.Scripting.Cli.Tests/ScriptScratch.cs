namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// Per-test scratch directory tree (root / script / out) on real disk. The CLI loads scripts
/// from disk through its bootstrap VFS, so a temp folder is the natural fixture.
/// </summary>
internal sealed class ScriptScratch : IDisposable
{
    public string Root { get; }
    public string ScriptDir { get; }
    public string OutDir { get; }

    public ScriptScratch()
    {
        Root = Path.Combine(Path.GetTempPath(), "ork-cli-cov-" + Guid.NewGuid().ToString("N"));
        ScriptDir = Path.Combine(Root, "script");
        OutDir = Path.Combine(Root, "out");
        Directory.CreateDirectory(ScriptDir);
        Directory.CreateDirectory(OutDir);
    }

    public string WriteScript(string fileName, string contents)
    {
        var path = Path.Combine(ScriptDir, fileName);
        File.WriteAllText(path, contents);
        return path;
    }

    public string WriteFile(string fileName, string contents)
    {
        var path = Path.Combine(Root, fileName);
        File.WriteAllText(path, contents);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best-effort */ }
    }
}
