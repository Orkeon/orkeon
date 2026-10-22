using Orkeon.Domain.FileSystem;

namespace Orkeon.Tests.Shared.Fixtures.DevRepo;

/// <summary>
/// Builds a small development repository (C#, TypeScript, Markdown, project files) through an
/// <see cref="IFileSystemService"/>, under a virtual directory. Project files exist at test time only.
/// </summary>
public sealed class DevRepoFixtureBuilder
{
    private readonly List<(string Path, string Content)> _files = [];
    private readonly List<(string Name, string[] References)> _projects = [];

    public DevRepoFixtureBuilder WithCSharpFile(string path, string source) => Add(path, source);

    public DevRepoFixtureBuilder WithTypeScriptFile(string path, string source) => Add(path, source);

    public DevRepoFixtureBuilder WithMarkdownFile(string path, string text) => Add(path, text);

    public DevRepoFixtureBuilder WithProject(string name, params string[] references)
    {
        _projects.Add((name, references));
        return this;
    }

    /// <summary>Writes every file under <paramref name="virtualRoot"/>; returns their virtual paths.</summary>
    public IReadOnlyList<string> Build(IFileSystemService fs, string virtualRoot = "/src")
    {
        var root = virtualRoot.TrimEnd('/');
        var written = new List<string>();
        foreach (var (path, content) in _files)
        {
            var virtualPath = $"{root}/{path}";
            fs.WriteAllTextAsync(virtualPath, content, CancellationToken.None).GetAwaiter().GetResult();
            written.Add(virtualPath);
        }
        foreach (var (name, references) in _projects)
        {
            var refs = string.Concat(references.Select(r =>
                $"    <ProjectReference Include=\"..\\{r}\\{r}.csproj\" />\n"));
            var csproj = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n" + refs + "  </ItemGroup>\n</Project>\n";
            var virtualPath = $"{root}/{name}/{name}.csproj";
            fs.WriteAllTextAsync(virtualPath, csproj, CancellationToken.None).GetAwaiter().GetResult();
            written.Add(virtualPath);
        }
        return written;
    }

    private DevRepoFixtureBuilder Add(string path, string content)
    {
        _files.Add((path, content));
        return this;
    }
}
