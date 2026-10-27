using System.Collections.Immutable;
using Orkeon.Cli.Commands.Scripting.Loading;
using Orkeon.Cli.Commands.Scripting.Registry;
using Orkeon.Cli.Commands.Scripting.Tests.Dispatch;
using Orkeon.Cli.Commands.Scripting.Tests.Fixtures;
using Orkeon.Scripting;
using Orkeon.Scripting.Toolchain;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Cli.Commands.Scripting.Tests.Runtime;

/// <summary>A scripted command's group: the declared one, else the folder holding its file, else none.</summary>
public sealed class ScriptCommandGroupTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-script-group-" + Guid.NewGuid().ToString("N"));
    private readonly ScriptedTestConsole _console = new();

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private static string Define(string name, string? group = null)
    {
        var groupField = group is null ? "" : $"group: \"{group}\",";
        return "defineCommand({ name: \"" + name + "\", description: \"d\", " + groupField + " handler: function () {} });";
    }

    private Task<ScriptCommandRegistry> LoadAsync(params (string RelativePath, string Source)[] files)
        => LoadFromAsync("/cmd", "/cmd", files);

    private async Task<ScriptCommandRegistry> LoadFromAsync(string mount, string directory, params (string RelativePath, string Source)[] files)
    {
        var ct = TestContext.Current.CancellationToken;
        foreach (var (path, source) in files)
        {
            var full = Path.Combine(_tempDir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, source, ct);
        }
        var loader = new ScriptCommandLoader(
            fileSystem: new DiskBackedFileSystemService(_tempDir, virtualRoot: mount),
            transpiler: PassThroughTranspiler.Instance,
            engineFactory: new JsEngineFactory(),
            options: new ScriptCommandLoaderOptions { Directories = ImmutableArray.Create(directory) });
        var (registry, _) = await loader.LoadAndRegisterAsync(ct);
        return registry;
    }

    private static string? GroupOf(ScriptCommandRegistry registry, string name)
        => registry.ScriptCommands.Single(c => c.Name == name).Group;

    [Fact]
    public async Task ShouldDeriveTheGroupFromTheFolder_WhenNoneIsDeclared()
    {
        var registry = await LoadAsync(("session/clear.cmd.ts", Define("wipe")), ("a/b/deep.cmd.ts", Define("deep")));

        Assert.Equal("session", GroupOf(registry, "wipe"));
        Assert.Equal("b", GroupOf(registry, "deep"));
    }

    [Fact]
    public async Task ShouldHaveNoGroup_WhenTheFileSitsAtTheMountRoot()
    {
        var registry = await LoadAsync(("root.cmd.ts", Define("rooted")));

        Assert.Null(GroupOf(registry, "rooted"));
    }

    [Fact]
    [Trait("BR", "#214")]
    public async Task ShouldTakeTheGroupRelativeToTheLoaderDirectory_WhenItIsNestedInTheMount()
    {
        var registry = await LoadFromAsync("/workspace", "/workspace/commands",
            ("commands/a.cmd.ts", Define("top")), ("commands/session/b.cmd.ts", Define("wipe")));

        Assert.Null(GroupOf(registry, "top"));
        Assert.Equal("session", GroupOf(registry, "wipe"));
    }

    [Fact]
    public async Task ShouldPreferTheDeclaredGroup_OverTheFolder()
    {
        var registry = await LoadAsync(("session/clear.cmd.ts", Define("wipe", group: "tidy")));

        Assert.Equal("tidy", GroupOf(registry, "wipe"));
    }

    [Fact]
    public async Task ShouldHaveNoGroup_WhenTheFolderIsNotAGroupName()
    {
        var registry = await LoadAsync(("My Folder/x.cmd.ts", Define("odd")));

        Assert.Null(GroupOf(registry, "odd"));
    }

    [Fact]
    public async Task HelpCmd_ShouldNameTheGroupOnItsFirstLine()
    {
        var registry = await LoadAsync(("session/clear.cmd.ts", Define("wipe")), ("root.cmd.ts", Define("rooted")));
        var help = new ScriptHelpCommand(() => registry.ScriptCommands);
        var ct = TestContext.Current.CancellationToken;

        await help.ExecuteAsync(ChainHarness.Ctx("help-cmd wipe", "wipe", _console), ct);
        var grouped = _console.Output;
        Assert.Contains("[session]", grouped.Split('\n')[0], StringComparison.Ordinal);

        var before = _console.Output.Length;
        await help.ExecuteAsync(ChainHarness.Ctx("help-cmd rooted", "rooted", _console), ct);
        var ungrouped = _console.Output[before..];
        Assert.DoesNotContain("[", ungrouped.Split('\n')[0], StringComparison.Ordinal);
    }
}
