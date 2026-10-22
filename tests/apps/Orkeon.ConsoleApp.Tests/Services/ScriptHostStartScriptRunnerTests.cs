using Orkeon.ConsoleApp.Services;
using Orkeon.Domain.Tools;
using Orkeon.Scripting;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.ConsoleApp.Tests.Services;

public sealed class ScriptHostStartScriptRunnerTests
{
    private static async Task<StartScriptOutput> RunAsync(string source)
    {
        const string path = "/workspace/start.ork.ts";
        var fs = new FakeFileSystemService().AddMount("/workspace").AddFile(path, source);
        var runner = new ScriptHostStartScriptRunner(new ScriptHost(fs, new JsEngineFactory()), fs, Array.Empty<IBaseTool>());
        return await runner.RunAsync(path, TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("BR", "RT-05")]
    public async Task ShouldStoreWhatTheScriptPrinted_WhenOrkTsScriptLogs()
    {
        var output = await RunAsync("console.log(\"open work: X\");");

        Assert.Contains("open work: X", output.Stdout);
    }

    [Fact]
    [Trait("BR", "RT-05")]
    public async Task ShouldNotStoreTypeName_WhenScriptReturnsAnObject()
    {
        var output = await RunAsync("({ open: 3 });");

        Assert.DoesNotContain("ExpandoObject", output.Stdout);
    }
}
