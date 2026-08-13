using System.Text;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// In-process behavioural tests for the diagnostic runner modes
/// (<see cref="RunnerExecution.RunValidateAsync"/> and
/// <see cref="RunnerExecution.RunListToolsAsync"/>). Each test builds a real host, so the
/// cases live in a single class (xUnit runs a class's tests sequentially) because they
/// redirect the process-global <see cref="Console"/> streams.
///
/// RaggableTree is disabled via a temp appsettings.json so the on-device embedding model
/// (ONNX) is never loaded — keeping the host lightweight and avoiding a native teardown.
/// </summary>
public sealed class RunnerExecutionDiagnosticsTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private readonly string _tempDir;

    public RunnerExecutionDiagnosticsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orkeon-hosting-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        // appsettings.json next to the config is auto-resolved by RunnerSettings; disabling
        // RaggableTree keeps local embeddings (ONNX) out of the host.
        File.WriteAllText(
            Path.Combine(_tempDir, "appsettings.json"),
            "{ \"RaggableTree\": { \"Enabled\": false } }");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    private string WriteConfig(string fileName, string yaml)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, yaml);
        return path;
    }

    private static async Task<(int exit, string stdout, string stderr)> CaptureAsync(Func<Task<int>> run)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        using var stdout = new StringWriter(new StringBuilder());
        using var stderr = new StringWriter(new StringBuilder());
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = await run().ConfigureAwait(false);
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    private const string OkCrewYaml = """
        name: "validate-ok"
        goal: "Minimal crew for the --validate smoke test"
        process: "sequential"
        agents:
          reader:
            role: "Reader"
            goal: "Read a file"
            backstory: "A minimal test agent."
            tools:
              - "file_read"
            maxIter: 1
        tasks:
          do_read:
            description: "Read a file and report its contents."
            expectedOutput: "The file contents."
            agent: "reader"
        """;

    private const string BogusToolCrewYaml = """
        name: "validate-bogus"
        goal: "Crew referencing a non-existent tool"
        process: "sequential"
        agents:
          reader:
            role: "Reader"
            goal: "Use a tool that does not exist"
            backstory: "A minimal test agent."
            tools:
              - "definitely_not_a_real_tool"
            maxIter: 1
        tasks:
          do_read:
            description: "Try to use a bogus tool."
            expectedOutput: "n/a"
            agent: "reader"
        """;

    [Fact]
    public async Task Validate_reports_ok_for_a_loadable_crew()
    {
        var configPath = WriteConfig("config.yaml", OkCrewYaml);
        var opts = new TestOptions { ConfigPath = configPath, AllowExternalMounts = true };

        var (exit, stdout, _) = await CaptureAsync(
            () => RunnerExecution.RunValidateAsync(opts, "Orkeon.Hosting.Tests"));

        Assert.Equal(0, exit);
        Assert.Contains("VALIDATION OK:", stdout, StringComparison.Ordinal);
        Assert.Contains("agents=1", stdout, StringComparison.Ordinal);
        Assert.Contains("tasks=1", stdout, StringComparison.Ordinal);
        Assert.Contains("tools resolved=1", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_fails_and_names_the_missing_tool()
    {
        var configPath = WriteConfig("config.yaml", BogusToolCrewYaml);
        var opts = new TestOptions { ConfigPath = configPath, AllowExternalMounts = true };

        var (exit, stdout, stderr) = await CaptureAsync(
            () => RunnerExecution.RunValidateAsync(opts, "Orkeon.Hosting.Tests"));

        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("VALIDATION OK:", stdout, StringComparison.Ordinal);
        Assert.Contains("VALIDATION FAILED:", stderr, StringComparison.Ordinal);
        Assert.Contains("definitely_not_a_real_tool", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds a crew directory that declares the agents twice — once flat (<c>agents.yaml</c>)
    /// and once per-entity (<c>agents/</c>) — which the loader rejects as a mixed layout.
    /// </summary>
    private string WriteMixedLayoutCrewDirectory()
    {
        var root = Path.Combine(_tempDir, "mixed-crew");
        Directory.CreateDirectory(Path.Combine(root, "agents"));
        Directory.CreateDirectory(Path.Combine(root, "tasks"));

        File.WriteAllText(Path.Combine(root, "config.yaml"), """
            name: "mixed-layout"
            goal: "Crew declaring its agents both flat and per-entity"
            process: "sequential"
            """);
        File.WriteAllText(Path.Combine(root, "agents.yaml"), """
            reader:
              role: "Reader"
              goal: "Read a file"
              backstory: "A minimal test agent."
              maxIter: 1
            """);
        File.WriteAllText(Path.Combine(root, "agents", "reader.yaml"), """
            role: "Reader"
            goal: "Read a file"
            backstory: "A minimal test agent."
            maxIter: 1
            """);
        File.WriteAllText(Path.Combine(root, "tasks", "do_read.yaml"), """
            description: "Read a file and report its contents."
            expectedOutput: "The file contents."
            agent: "reader"
            """);

        return root;
    }

    [Fact]
    public async Task Validate_reports_a_crew_configuration_error_without_a_stack_trace()
    {
        var opts = new TestOptions
        {
            ConfigPath = WriteMixedLayoutCrewDirectory(),
            SettingsPath = Path.Combine(_tempDir, "appsettings.json"),
            AllowExternalMounts = true,
        };

        var (exit, _, stderr) = await CaptureAsync(
            () => RunnerExecution.RunValidateAsync(opts, "Orkeon.Hosting.Tests"));

        Assert.Equal(1, exit);
        Assert.Contains("VALIDATION FAILED:", stderr, StringComparison.Ordinal);
        Assert.Contains("Mixed crew layout", stderr, StringComparison.Ordinal);
        // The message tells the user what to fix; the exception type and its stack are
        // debugging noise that used to bury it at the default verbosity.
        Assert.DoesNotContain("System.InvalidOperationException", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("   at Orkeon.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_keeps_the_stack_trace_when_verbose()
    {
        var opts = new TestOptions
        {
            ConfigPath = WriteMixedLayoutCrewDirectory(),
            SettingsPath = Path.Combine(_tempDir, "appsettings.json"),
            AllowExternalMounts = true,
            Verbose = 1,
        };

        var (exit, _, stderr) = await CaptureAsync(
            () => RunnerExecution.RunValidateAsync(opts, "Orkeon.Hosting.Tests"));

        Assert.Equal(1, exit);
        Assert.Contains("Mixed crew layout", stderr, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException", stderr, StringComparison.Ordinal);
        Assert.Contains("   at Orkeon.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Validate_returns_error_when_config_missing()
    {
        var opts = new TestOptions { ConfigPath = "" };

        var (exit, _, stderr) = await CaptureAsync(
            () => RunnerExecution.RunValidateAsync(opts, "Orkeon.Hosting.Tests"));

        Assert.NotEqual(0, exit);
        Assert.Contains("--config is required", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListTools_prints_sorted_registry_names_on_stdout()
    {
        // No crew needed; point --settings at the RaggableTree-disabled temp appsettings.
        var opts = new TestOptions
        {
            SettingsPath = Path.Combine(_tempDir, "appsettings.json"),
            AllowExternalMounts = true,
        };

        var (exit, stdout, _) = await CaptureAsync(
            () => RunnerExecution.RunListToolsAsync(opts, "Orkeon.Hosting.Tests"));

        Assert.Equal(0, exit);

        var lines = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        Assert.NotEmpty(lines);
        // Base-host tools that are always registered by RunnerHost.
        Assert.Contains("file_read", lines);
        Assert.Contains("publish_event", lines);
        // human_input is injected on the crew path (TryBuildHost) — --list-tools must mirror it
        // so the manifest lists exactly the tools a real kickoff exposes.
        Assert.Contains("human_input", lines);
        // stdout must carry ONLY tool names — one token per line, no whitespace/log noise.
        Assert.All(lines, l => Assert.DoesNotContain(' ', l));
        // Names are emitted sorted (ordinal).
        var sorted = lines.OrderBy(l => l, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, lines);
    }
}
