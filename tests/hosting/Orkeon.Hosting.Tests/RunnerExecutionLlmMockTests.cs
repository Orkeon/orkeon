using Microsoft.Extensions.DependencyInjection;
using Orkeon.Constants.FileSystem;
using Orkeon.Domain.FileSystem;
using Orkeon.Hosting.Tests.Doubles;
using Orkeon.Infrastructure.FileSystem;

namespace Orkeon.Hosting.Tests;

/// <summary>
/// <c>--llm-mock</c> on a YAML crew, end to end: the crew's agent reaches its mocked answer, and a
/// real built-in tool it asks for runs, through <c>LlmProviderFactory</c> and its call-mode decorator,
/// without a model and without a network. Joins <see cref="ConsoleSerialCollection"/>: the runner
/// writes some of its lines on the process-global console.
/// </summary>
[Collection(ConsoleSerialCollection.Name)]
public sealed class RunnerExecutionLlmMockTests : IDisposable
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private readonly string _root;
    private readonly string _crewDir;
    private readonly string _outDir;
    private readonly string _mockDir;

    public RunnerExecutionLlmMockTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "orkeon-hosting-llm-mock-" + Guid.NewGuid().ToString("N"));
        _crewDir = Path.Combine(_root, "crew");
        _outDir = Path.Combine(_root, "out");
        _mockDir = Path.Combine(_root, "mock");
        Directory.CreateDirectory(_crewDir);
        Directory.CreateDirectory(_outDir);
        Directory.CreateDirectory(_mockDir);
        File.WriteAllText(Path.Combine(_crewDir, "config.yaml"), """
            name: "mock-crew"
            goal: "Write one report"
            process: "sequential"
            agents:
              scribe:
                role: "Scribe"
                goal: "Write the report"
                backstory: "A minimal test agent."
                tools:
                  - "file_write"
                maxIter: 6
            tasks:
              write_it:
                description: "Write the report to the output folder."
                expectedOutput: "A confirmation."
                agent: "scribe"
            """);
        File.WriteAllText(Path.Combine(_crewDir, "appsettings.json"), """{ "RaggableTree": { "Enabled": false } }""");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
        catch (UnauthorizedAccessException) { /* best-effort temp cleanup */ }
    }

    private string WriteResponses(params string[] lines)
    {
        var path = Path.Combine(_mockDir, "answers.jsonl");
        File.WriteAllText(path, string.Join('\n', lines) + "\n");
        return path;
    }

    private async Task<(int Exit, string Out, string Err)> RunAsync(string responses)
    {
        var console = new FakeRunnerConsole();
        var opts = new TestOptions
        {
            ConfigPath = Path.Combine(_crewDir, "config.yaml"),
            SettingsPath = Path.Combine(_crewDir, "appsettings.json"),
            Mounts = [$"{_outDir}:/output:rw"],
            AllowExternalMounts = true,
            LlmMock = responses,
            RunnerConsole = console,
        };

        var exit = await RunnerExecution.RunOneShotAsync(opts, "Orkeon.Hosting.Tests", null, TestContext.Current.CancellationToken);
        return (exit, console.Output.ToString(), console.ErrorOutput.ToString());
    }

    [Fact]
    public async Task A_yaml_crew_ends_on_the_mocked_answer_after_the_mocked_tool_call_ran_the_real_tool()
    {
        var responses = WriteResponses(
            """{"match":"Write the report","toolCalls":[{"name":"file_write","arguments":{"path":"/output/report.txt","content":"written by the mock"}}]}""",
            """{"match":"Write the report","content":"The report is in the output folder."}""");

        var (exit, stdout, stderr) = await RunAsync(responses);

        Assert.True(exit == 0, stderr);
        Assert.Equal("written by the mock", await File.ReadAllTextAsync(Path.Combine(_outDir, "report.txt"), TestContext.Current.CancellationToken));
        Assert.Contains("The report is in the output folder.", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_successive_mocked_tool_calls_both_run_before_the_final_answer()
    {
        var responses = WriteResponses(
            """{"match":"Write the report","toolCalls":[{"name":"file_write","arguments":{"path":"/output/one.txt","content":"first"}}]}""",
            """{"match":"Write the report","toolCalls":[{"name":"file_write","arguments":{"path":"/output/two.txt","content":"second"}}]}""",
            """{"match":"Write the report","content":"Both files are written."}""");

        var (exit, stdout, stderr) = await RunAsync(responses);

        Assert.True(exit == 0, stderr);
        Assert.Equal("first", await File.ReadAllTextAsync(Path.Combine(_outDir, "one.txt"), TestContext.Current.CancellationToken));
        Assert.Equal("second", await File.ReadAllTextAsync(Path.Combine(_outDir, "two.txt"), TestContext.Current.CancellationToken));
        Assert.Contains("Both files are written.", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_prompt_no_line_fits_fails_the_crew_with_the_prompt_instead_of_a_green_empty_answer()
    {
        var responses = WriteResponses("""{"match":"something the crew never asks","content":"unused"}""");

        var (exit, _, stderr) = await RunAsync(responses);

        Assert.Equal(2, exit);
        Assert.Contains("No line of /llm-mock/answers.jsonl matches the prompt", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_llm_mock_folder_is_mounted_read_only_for_the_vfs()
    {
        var responses = WriteResponses("""{"match":"x","content":"y"}""");
        var physical = RunnerLlmMock.ResolvePath(responses);
        using var host = RunnerHost.Build(
            null,
            new RunnerMountPlan
            {
                CliMounts = [$"{_outDir}:/output:rw"],
                InternalMounts = RunnerLlmMock.CreateMounts(physical),
                InputDirectories = RunnerLlmMock.InputDirectories(physical),
                AllowExternalMounts = true,
                LlmMockResponses = RunnerLlmMock.VirtualPath(physical),
            });
        var fileSystem = host.Services.GetRequiredService<PrivilegedFileSystemAccess>().FileSystem;

        var read = await fileSystem.TryReadAllTextAsync("/llm-mock/answers.jsonl", TestContext.Current.CancellationToken);
        var refusal = await Assert.ThrowsAsync<FileAccessDeniedException>(() =>
            fileSystem.WriteAllTextAsync("/llm-mock/injected.txt", "x", TestContext.Current.CancellationToken));

        Assert.NotNull(read);
        Assert.Contains("/llm-mock/injected.txt", refusal.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_mockDir, "injected.txt")));
    }

    [Fact]
    public void A_profile_naming_a_provider_other_than_mock_is_a_settings_refusal_naming_the_key()
    {
        var settings = Path.Combine(_crewDir, "bad.json");
        File.WriteAllText(settings, """{ "Llm": { "Profiles": { "a": { "Provider": "openai" } } } }""");

        var refusal = Assert.Throws<RunnerSettingsException>(() =>
            RunnerHost.Build(settings, new RunnerMountPlan { CliMounts = [$"{_outDir}:/output:rw"], AllowExternalMounts = true }));

        Assert.Contains("Llm:Profiles:a:Provider", refusal.Message, StringComparison.Ordinal);
    }
}
