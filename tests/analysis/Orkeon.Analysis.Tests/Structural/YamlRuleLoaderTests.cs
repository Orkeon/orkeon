using System.Text;
using Orkeon.Analysis.Core.Structural;
using Orkeon.Analysis.TreeSitter;
using Orkeon.Domain.FileSystem;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Analysis.Tests.Structural;

public sealed class YamlRuleLoaderTests
{
    private static readonly string[] HarnessRules = ["await-member", "await-member-in-lambda", "await-member-in-loop", "memory-filter"];

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "DevRepo", "Rules", name + ".yml"));

    [Fact]
    [Trait("BR", "RT-31")]
    public async Task ShouldLoadRuleFromYamlFile_WhenPathUnderWorkspace()
    {
        var fs = new FakeFileSystemService().AddMount("/workspace");
        fs.AddFile("/workspace/rules/r.yml", "id: no-log\nlanguage: cs\nseverity: warning\nmessage: Do not log directly\nrule:\n  pattern: Log.Write($X)\nfix: Logger.Info($X)\n");

        var rule = await YamlRuleLoader.LoadAsync(fs, "/workspace/rules/r.yml", TestContext.Current.CancellationToken);

        Assert.Equal("no-log", rule.Id);
        Assert.Equal("csharp", rule.Language);
        Assert.Equal("warning", rule.Severity);
        Assert.Equal("Do not log directly", rule.Message);
        Assert.Equal("Logger.Info($X)", rule.Fix);
        using var pool = new TreeSitterParserPool();
        var hits = new Matcher(pool).Match("/workspace/a.cs", Encoding.UTF8.GetBytes("class C { void M() { Log.Write(1); Other(2); } }"), rule);
        Assert.Equal("Log.Write(1)", Assert.Single(hits).Text);
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public void ShouldParseHarnessAccessCostRules_WhenLoadedAsFixtures()
    {
        foreach (var name in HarnessRules)
        {
            var rule = StructuralRule.FromYaml(ReadFixture(name));
            Assert.Equal("csharp", rule.Language);
            Assert.Equal(name, rule.Id);
        }

        var loop = StructuralRule.FromYaml(ReadFixture("await-member-in-loop"));
        const string source = "class C { async Task M() { foreach (var x in xs) { await _repo.GetAsync(x); } await _repo.GetAsync(1); } }";
        using var pool = new TreeSitterParserPool();
        var hits = new Matcher(pool).Match("/workspace/a.cs", Encoding.UTF8.GetBytes(source), loop);
        Assert.Equal("await _repo.GetAsync(x)", Assert.Single(hits).Text);
    }

    [Fact]
    [Trait("BR", "RT-31")]
    public async Task ShouldRefuseRuleFile_WhenPathIsOutsideEveryMount()
    {
        var fs = new FakeFileSystemService().AddMount("/workspace");

        var ex = await Assert.ThrowsAsync<FileAccessDeniedException>(
            () => YamlRuleLoader.LoadAsync(fs, "/etc/rules.yml", TestContext.Current.CancellationToken));

        Assert.Equal("No mount for virtual path '/etc/rules.yml'.", ex.Message);
    }
}
