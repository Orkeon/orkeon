using System.Text.Json;
using Orkeon.Constants.Configuration;
using Orkeon.Hosting;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// <c>orkeon settings env</c>: the environment variables Orkeon reads, in the terminal — the ones
/// that carry a setting, the ones a binary reads by their name and the ones a setting names —,
/// from the table the reference page is held to (<see cref="EnvironmentVariableNames"/>).
/// </summary>
[Collection(CliCollection.Name)]
public sealed class SettingsEnvironmentCommandTests
{
    private const int Columns = 100;

    [Fact]
    public async Task Env_lists_every_variable_read_by_its_name_with_who_reads_it_and_what_it_does()
    {
        var (exit, stdout, stderr) = await RunAsync("settings", "env");

        Assert.Equal(Program.ExitOk, exit);
        Assert.Empty(stderr);
        var lines = Lines(stdout);
        Assert.All(lines, line => Assert.True(line.Length <= Columns, $"{line.Length} columns: {line}"));
        foreach (var entry in EnvironmentVariableNames.ReadByName)
        {
            var head = Array.FindIndex(lines, line => line.StartsWith($"  {entry.Name} ", StringComparison.Ordinal));
            Assert.True(head >= 0, $"no line for {entry.Name} in:{Environment.NewLine}{stdout}");
            // Under the name: who reads it, then what it does — wrapped, so read back as one text.
            var under = lines.Skip(head + 1).TakeWhile(line => line.StartsWith("      ", StringComparison.Ordinal));
            Assert.Equal(
                $"Read by: {string.Join(", ", entry.ReadBy)} {entry.Effect}",
                string.Join(' ', under.Select(line => line.Trim())));
        }
    }

    [Fact]
    public async Task Env_gives_the_rule_of_the_variables_that_carry_a_setting_and_the_keys_that_name_one()
    {
        var (_, stdout, _) = await RunAsync("settings", "env");

        Assert.Contains("ORKEON_<Section>__<Key>", stdout, StringComparison.Ordinal);
        Assert.Contains("ORKEON_Llm__Model", stdout, StringComparison.Ordinal);
        Assert.Contains("ORKEON_RateLimiting__MaxConcurrentRequests", stdout, StringComparison.Ordinal);
        Assert.Contains("ORKEON_Orkeon__Rag__Profile", stdout, StringComparison.Ordinal);
        var lines = Lines(stdout);
        var naming = EnvironmentVariablesReferenceTests.NamingKeys();
        Assert.Contains("Llm:ApiKeyEnvVar", naming);
        Assert.Contains("Orkeon:Host:Discord:TokenEnvironmentVariable", naming);
        Assert.All(naming, key => Assert.Contains("  " + key, lines));
        Assert.EndsWith("#environment-variables", lines[^1], StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>env</c> is the table and not the word: what the word would have found — a key named with
    /// it, such as the environment of an MCP server — follows the table, so nothing is hidden.
    /// </summary>
    [Fact]
    public async Task Env_is_the_table_and_still_says_what_the_word_names_among_the_settings()
    {
        var (exit, stdout, _) = await RunAsync("settings", "ENV");

        Assert.Equal(Program.ExitOk, exit);
        Assert.Contains(EnvironmentVariableNames.Debug, stdout, StringComparison.Ordinal);
        Assert.Contains("Named or described with \"env\"", stdout, StringComparison.Ordinal);
        Assert.Contains(Lines(stdout), line => line.StartsWith("  MCP:Servers:<name>:Env:<name> ", StringComparison.Ordinal));

        // A longer word is a word again: the table is for `env` alone.
        var (_, word, _) = await RunAsync("settings", "environment");
        Assert.DoesNotContain(EnvironmentVariableNames.TuiDriver, word, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Env_as_json_is_the_table_for_a_program()
    {
        var (exit, stdout, _) = await RunAsync("settings", "env", "--json");

        Assert.Equal(Program.ExitOk, exit);
        Assert.DoesNotContain('\r', stdout);
        using var document = JsonDocument.Parse(stdout);
        var variables = document.RootElement.GetProperty("variables").EnumerateArray().ToList();
        Assert.Equal(
            EnvironmentVariableNames.ReadByName.Select(entry => entry.Name),
            variables.Select(variable => variable.GetProperty("name").GetString()));
        var debug = variables.Single(variable => variable.GetProperty("name").GetString() == EnvironmentVariableNames.Debug);
        Assert.Equal([SettingsHosts.Cli], debug.GetProperty("readBy").EnumerateArray().Select(binary => binary.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(debug.GetProperty("value").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(debug.GetProperty("effect").GetString()));
        Assert.Equal(EnvironmentVariableNames.SettingsPrefix, document.RootElement.GetProperty("settingsPrefix").GetString());
        Assert.Equal(
            EnvironmentVariablesReferenceTests.NamingKeys(),
            document.RootElement.GetProperty("namedBySettings").EnumerateArray().Select(key => key.GetString()));
    }

    [Fact]
    public async Task Env_takes_no_option_but_json()
    {
        foreach (var option in new string[][] { ["--all"], ["--host", "run"], ["--all", "--json"] })
        {
            var (exit, stdout, stderr) = await RunAsync(["settings", "env", .. option]);

            Assert.Equal(Program.ExitScriptError, exit);
            Assert.Empty(stdout);
            Assert.Contains("env", stderr, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_help_and_the_categories_name_the_form()
    {
        var (_, help, _) = await RunAsync("settings", "--help");
        var (_, categories, _) = await RunAsync("settings");

        Assert.Contains(Lines(help), line => line.StartsWith("  orkeon settings env ", StringComparison.Ordinal));
        Assert.Contains(Lines(categories), line => line.StartsWith("  orkeon settings env ", StringComparison.Ordinal));
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> RunAsync(params string[] args)
    {
        using var console = new TestConsole();
        var exit = await Program.DispatchAsync(args);
        return (exit, console.Stdout, console.Stderr);
    }

    private static string[] Lines(string text) =>
        text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
}
