using System.Text.Json;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// FWK-18 — <c>orkeon mcp serve --expose-output --max-concurrency</c> through the real entry point:
/// the host is built over a settings file, the requests go in on stdin and the answers come back on
/// stdout. <c>/output</c> is served through the host's virtual file system; <c>/workspace</c> and
/// physical paths never are.
/// </summary>
[Collection(CliCollection.Name)]
public sealed class McpServeResourcesTests
{
    private const string List = """{"jsonrpc":"2.0","id":1,"method":"resources/list"}""";

    private const string ReadOutput = """{"jsonrpc":"2.0","id":2,"method":"resources/read","params":{"uri":"orkeon:///output/report.md"}}""";

    private const string ReadWorkspace = """{"jsonrpc":"2.0","id":3,"method":"resources/read","params":{"uri":"orkeon:///workspace/secret.txt"}}""";

    /// <summary>A settings file mounting a folder holding <c>report.md</c> read-only at <c>/output</c>; the working directory holds <c>secret.txt</c>.</summary>
    private static async Task<(string Settings, string Cwd, string OutputFolder)> SeedAsync(ScriptScratch scratch)
    {
        var cwd = Path.Combine(scratch.Root, "cwd");
        var output = Path.Combine(scratch.Root, "published");
        Directory.CreateDirectory(cwd);
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(cwd, "secret.txt"), "top secret", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(output, "report.md"), "# report", TestContext.Current.CancellationToken);
        var mount = JsonSerializer.Serialize($"{output}:/output:ro");
        var settings = scratch.WriteFile("appsettings.json", $$"""{ "Orkeon": { "FileSystem": { "Mounts": [ {{mount}} ] } } }""");
        return (settings, cwd, output);
    }

    [Fact]
    public async Task ExposeOutput_serves_the_output_mount_and_never_the_workspace()
    {
        using var scratch = new ScriptScratch();
        var (settings, cwd, output) = await SeedAsync(scratch);
        using var console = new TestConsole(stdin: List + "\n" + ReadOutput + "\n" + ReadWorkspace + "\n");

        var exit = await McpCommand.ServeAsync(console, new McpServeCommandOptions
        {
            SettingsPath = settings,
            WorkingDirectoryOverride = cwd,
            ExposeOutput = true,
        });

        Assert.Equal(Program.ExitOk, exit);
        var answers = Answers(console.Stdout).ToDictionary(a => a.GetProperty("id").GetInt32());
        Assert.Equal(
            ["orkeon:///output/report.md"],
            Result(answers[1]).GetProperty("resources").EnumerateArray().Select(r => r.GetProperty("uri").GetString()));
        Assert.Equal("# report", Result(answers[2]).GetProperty("contents")[0].GetProperty("text").GetString());
        Assert.True(answers[3].TryGetProperty("error", out _));
        Assert.DoesNotContain("top secret", console.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(output, console.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(scratch.Root, console.Stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_flag_on_the_command_line_exposes_output()
    {
        using var scratch = new ScriptScratch();
        var (settings, _, _) = await SeedAsync(scratch);
        using var console = new TestConsole(stdin: List + "\n");

        var exit = await Program.DispatchAsync(console, ["mcp", "serve", "--settings", settings, "--expose-output", "--max-concurrency", "2"]);

        Assert.Equal(Program.ExitOk, exit);
        var answer = Assert.Single(Answers(console.Stdout));
        Assert.Equal(1, Result(answer).GetProperty("resources").GetArrayLength());
    }

    [Fact]
    public async Task Without_the_flag_no_resource_is_served()
    {
        using var scratch = new ScriptScratch();
        var (settings, _, _) = await SeedAsync(scratch);
        using var console = new TestConsole(stdin: List + "\n" + ReadOutput + "\n");

        var exit = await Program.DispatchAsync(console, ["mcp", "serve", "--settings", settings]);

        Assert.Equal(Program.ExitOk, exit);
        var answers = Answers(console.Stdout);
        Assert.Equal(2, answers.Count);
        Assert.All(answers, answer =>
            Assert.Equal(-32601, answer.GetProperty("error").GetProperty("code").GetInt32()));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public async Task A_bound_below_one_refuses_the_start_and_leaves_stdout_to_the_protocol(string bound)
    {
        using var scratch = new ScriptScratch();
        var settings = scratch.WriteFile("appsettings.json", "{}");
        using var console = new TestConsole(stdin: List + "\n");

        var exit = await Program.DispatchAsync(console, ["mcp", "serve", "--settings", settings, $"--max-concurrency={bound}"]);

        Assert.Equal(Program.ExitScriptError, exit);
        Assert.Contains("--max-concurrency", console.Stderr, StringComparison.Ordinal);
        Assert.Empty(console.Stdout);
    }

    [Fact]
    public async Task Help_names_the_two_options()
    {
        using var console = new TestConsole();

        await Program.DispatchAsync(console, ["mcp", "serve", "--help"]);

        Assert.Contains("--max-concurrency", console.Stderr, StringComparison.Ordinal);
        Assert.Contains("--expose-output", console.Stderr, StringComparison.Ordinal);
        Assert.Empty(console.Stdout);
    }

    private static JsonElement Result(JsonElement answer)
    {
        Assert.True(answer.TryGetProperty("result", out var result), answer.GetRawText());
        return result;
    }

    private static List<JsonElement> Answers(string stdout) =>
        stdout.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0)
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                Assert.Equal("2.0", document.RootElement.GetProperty("jsonrpc").GetString());
                return document.RootElement.Clone();
            }).ToList();
}
