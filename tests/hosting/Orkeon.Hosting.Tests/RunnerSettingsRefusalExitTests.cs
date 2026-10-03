using System.Text;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// GAP-35 — a setting the runner host refuses ends <c>orkeon run</c>, <c>--validate</c> and
/// <c>--list-tools</c> the way <c>cli.md</c> promises: one line, <c>ERROR: …</c>, the last on
/// stderr, naming the key or the file, and exit code 1 — never an unhandled exception, a stack
/// and a core dump (code 134, measured by GAP-24 on a retired key). In the console-serial
/// collection: the runs redirect the process-global console.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerSettingsRefusalExitTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase;

    private const string Crew = """
        name: "refused-settings"
        goal: "A crew the refused settings never let load"
        process: "sequential"
        agents:
          reader:
            role: "Reader"
            goal: "Read"
            backstory: "A minimal test agent."
            maxIter: 1
        tasks:
          read:
            description: "Read something."
            expectedOutput: "Something read."
            agent: "reader"
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "orkeon-refused-exit-" + Guid.NewGuid().ToString("N"));

    public RunnerSettingsRefusalExitTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    /// <summary>Each refused setting, and what the line must name.</summary>
    public static TheoryData<string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var mode in new[] { "run", "validate", "list-tools" })
        {
            data.Add(mode, """{ "RaggableTree": { "Exclude": ["bin"] } }""", "RaggableTree:Exclude");
            data.Add(mode, "{ \"Llm\": { \"Model\": \"gpt\" ,, } }", "refused.json");
            data.Add(mode, """{ "RaggableTree": { "Embedding": { "Provider": "Ollama", "BaseUrl": "pas une url" } } }""", "RaggableTree:Embedding:BaseUrl");
            data.Add(mode, """{ "RaggableTree": { "Enabled": false }, "Telemetry": { "OtlpEndpoint": "::" } }""", "Telemetry:OtlpEndpoint");
            data.Add(mode, """{ "RaggableTree": { "Enabled": false }, "Telemetry": { "ExportToConsole": true } }""", "Telemetry:ExportToConsole");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_refused_setting_exits_1_with_one_last_line_naming_it(string mode, string json, string named)
    {
        var settings = Path.Combine(_root, "refused.json");
        await File.WriteAllTextAsync(settings, json, TestContext.Current.CancellationToken);
        var crew = Path.Combine(_root, "crew.yaml");
        await File.WriteAllTextAsync(crew, Crew, TestContext.Current.CancellationToken);
        var options = new TestOptions
        {
            ConfigPath = mode == "list-tools" ? "" : crew,
            SettingsPath = settings,
            AllowExternalMounts = true,
            Validate = mode == "validate",
            ListTools = mode == "list-tools",
        };

        var (exit, stdout, stderr) = await CaptureAsync(() => RunnerExecution.RunOneShotAsync(options, "Orkeon.Hosting.Tests"));

        Assert.True(exit == 1, $"exit {exit} — stderr: {stderr}");
        var last = stderr.TrimEnd().Split('\n')[^1].TrimEnd('\r');
        Assert.StartsWith("ERROR: ", last, StringComparison.Ordinal);
        Assert.Contains(named, last, StringComparison.Ordinal);
        // stdout is a protocol for --list-tools (the manifest): a refusal writes nothing there.
        Assert.Equal(string.Empty, stdout);
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> CaptureAsync(Func<Task<int>> run)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter(new StringBuilder());
        using var stderr = new StringWriter(new StringBuilder());
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await run();
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
