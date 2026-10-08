using Orkeon.Cli.TerminalGui.Hosting;

namespace Orkeon.ConsoleApp.Tests.DependencyInjection;

/// <summary>
/// GAP-40, decision 7 — the REPL starts behind the runner host's barrier: a setting it refuses — a
/// value, a key, a name, an <c>Orkeon:Rag:LlmProfile</c> it does not offer, a <c>--settings</c> file it
/// cannot read — is one line, <c>orkeon-repl: …</c>, and exit code 1, and no console opens. It used to
/// open on a file <c>orkeon run</c> refuses, and its first command failed on it — or the start crashed
/// on an unhandled exception.
/// </summary>
public sealed class ReplStartupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("orkeon-repl-startup-").FullName;
    private readonly List<string> _reported = [];

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private ReplLaunch Start(string json)
    {
        var settings = Path.Combine(_root, "repl.json");
        File.WriteAllText(settings, json);
        return ReplStartup.Prepare(["--settings", settings, "--mount", $"{_root}:/workspace:ro"], UiMode.Plain, replWordWrap: false, _reported.Add);
    }

    [Theory]
    [InlineData("""{ "Orkeon": { "Guardian": { "Enabled": "oui" } } }""", "Orkeon:Guardian:Enabled")]
    [InlineData("""{ "Orkeon": { "Guardain": { "Enabled": true } } }""", "Orkeon:Guardain")]
    [InlineData("""{ "Orkeon": { "Rag": { "LlmProfile": "nope" } } }""", "nope")]
    [InlineData("""{ "Llm": { "BaseUrl": "http://localhost:11434", "TimeoutSeconds": "600s" } }""", "Llm:TimeoutSeconds")]
    [InlineData("""{ "Logging": { "LogLevel": { "Default": "Informations" } } }""", "Logging:LogLevel:Default")]
    [InlineData("""{ "Orkeon": { "Cli": { "Tui": { "SpinerVerbs": [ "thinking" ] } } } }""", "Orkeon:Cli:Tui:SpinerVerbs")]
    public void A_refused_setting_is_one_line_and_exit_1_without_a_console(string json, string named)
    {
        var launch = Start(json);

        Assert.Null(launch.Host);
        Assert.Equal(1, launch.ExitCode);
        var line = Assert.Single(_reported);
        Assert.StartsWith("orkeon-repl: ", line, StringComparison.Ordinal);
        Assert.Contains(named, line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_settings_file_that_is_not_JSON_is_named_with_its_line()
    {
        var launch = Start("""
            {
              "Llm": { "Model": "gpt" ,, }
            }
            """);

        Assert.Null(launch.Host);
        Assert.Equal(1, launch.ExitCode);
        var line = Assert.Single(_reported);
        Assert.Contains("repl.json", line, StringComparison.Ordinal);
        Assert.Contains("line 2", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepted_settings_yield_the_host_and_report_nothing()
    {
        var launch = Start("""{ "Orkeon": { "Cli": { "Tui": { "SpinnerVerbs": [ "thinking" ] } } } }""");

        using var host = launch.Host;
        Assert.NotNull(host);
        Assert.Equal(0, launch.ExitCode);
        Assert.Empty(_reported);
    }

    /// <summary>
    /// A section Orkeon knows and no shipped binary reads refuses nothing: it is one line before the
    /// console opens, as a runner writes it on stderr, and the console opens.
    /// </summary>
    [Fact]
    public void A_section_no_shipped_binary_reads_is_one_warning_line_and_the_host_is_yielded()
    {
        var launch = Start("""{ "ToolRateLimiting": { "GlobalToolRequestsPerMinute": 10 } }""");

        using var host = launch.Host;
        Assert.NotNull(host);
        Assert.Equal(0, launch.ExitCode);
        var line = Assert.Single(_reported);
        Assert.StartsWith("orkeon-repl: warning: ToolRateLimiting is read by no component of this host", line, StringComparison.Ordinal);
        Assert.Contains("AddOrkeonToolRateLimiting()", line, StringComparison.Ordinal);
    }
}
