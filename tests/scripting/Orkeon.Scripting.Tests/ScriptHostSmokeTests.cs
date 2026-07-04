using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Scripting.Tests;

public sealed class ScriptHostSmokeTests
{
    private static ScriptHost CreateHost(out string virtualRoot)
    {
        var fixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        virtualRoot = "/scripts";
        var fs = new DiskBackedFileSystemService(fixturesDir, virtualRoot);
        return new ScriptHost(fs, new JsEngineFactory());
    }

    [Fact]
    public async Task RunAsync_returns_last_expression_value()
    {
        var host = CreateHost(out var root);

        var result = await host.RunAsync($"{root}/hello.ork.ts", CancellationToken.None);

        Assert.Equal("hello", result);
    }

    [Fact]
    public async Task RunAsync_falls_back_to_result_variable_when_last_expression_is_undefined()
    {
        var host = CreateHost(out var root);

        var result = await host.RunAsync($"{root}/result-variable.ork.ts", CancellationToken.None);

        Assert.Equal(42d, result);
    }

    [Fact]
    public async Task RunAsync_throws_FileNotFoundException_for_missing_script()
    {
        var host = CreateHost(out var root);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => host.RunAsync($"{root}/does-not-exist.ork.ts", CancellationToken.None));
    }
}
