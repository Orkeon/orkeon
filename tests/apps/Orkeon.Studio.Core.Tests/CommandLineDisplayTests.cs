using Orkeon.Studio.Core.Launch;

namespace Orkeon.Studio.Core.Tests;

/// <summary>
/// Quoting is a display concern only — the runner passes the argument list itself — but the
/// rendered line must be one a user could paste into their shell unchanged.
/// </summary>
public sealed class CommandLineDisplayTests
{
    private static readonly string[] RunWithSpacedPath = ["run", "/crews/my crew.yaml", "--verbose", "1"];

    private static readonly string[] RunCrewYaml = ["run", "crew.yaml"];

    [Theory]
    [InlineData("crew.yaml", "crew.yaml")]
    [InlineData("/srv/data:/workspace:ro", "/srv/data:/workspace:ro")]
    [InlineData("TOPIC=quantum computing", "'TOPIC=quantum computing'")]
    [InlineData("{\"topic\":\"rag\"}", "'{\"topic\":\"rag\"}'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("", "''")]
    public void Posix_quoting_only_quotes_what_a_shell_would_reinterpret(string argument, string expected)
    {
        Assert.Equal(expected, CommandLineDisplay.Quote(argument, CommandLineQuotingStyle.Posix));
    }

    [Theory]
    [InlineData("crew.yaml", "crew.yaml")]
    [InlineData(@"C:\crews\crew.yaml", @"C:\crews\crew.yaml")]
    [InlineData(@"C:\my crews\crew.yaml", "\"C:\\my crews\\crew.yaml\"")]
    [InlineData("{\"topic\":\"rag\"}", "\"{\\\"topic\\\":\\\"rag\\\"}\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    [InlineData("", "\"\"")]
    public void Windows_quoting_escapes_quotes_and_trailing_backslashes(string argument, string expected)
    {
        Assert.Equal(expected, CommandLineDisplay.Quote(argument, CommandLineQuotingStyle.Windows));
    }

    [Fact]
    public void A_command_line_joins_the_executable_and_its_quoted_arguments()
    {
        var line = CommandLineDisplay.Format(RunWithSpacedPath, style: CommandLineQuotingStyle.Posix);

        Assert.Equal("orkeon run '/crews/my crew.yaml' --verbose 1", line);
    }

    [Fact]
    public void The_executable_can_be_a_full_path_and_is_quoted_too()
    {
        var line = CommandLineDisplay.Format(
            RunCrewYaml,
            "/opt/orkeon tools/orkeon",
            CommandLineQuotingStyle.Posix);

        Assert.Equal("'/opt/orkeon tools/orkeon' run crew.yaml", line);
    }

    [Fact]
    public void Auto_follows_the_running_platform()
    {
        var expected = OperatingSystem.IsWindows()
            ? CommandLineQuotingStyle.Windows
            : CommandLineQuotingStyle.Posix;

        Assert.Equal(
            CommandLineDisplay.Quote("a b", expected),
            CommandLineDisplay.Quote("a b", CommandLineQuotingStyle.Auto));
    }
}
