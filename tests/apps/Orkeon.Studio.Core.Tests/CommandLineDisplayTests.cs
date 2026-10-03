using Orkeon.Studio.Core.Launch;
using Orkeon.Tests.Shared.Launchers;

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

    /// <summary>
    /// STUDIO-51, decision 9: the Windows line is one for the <c>cmd</c> prompt — the composer of the
    /// team launchers writes each argument (<c>TeamLauncherScript.QuoteForCmdPrompt</c>). A word
    /// without a special character stays bare; a value with <c>&amp;</c> or <c>^</c> goes between
    /// quotes; a value with a quote or a <c>%</c> goes outside them, every quote and operator
    /// escaped, the character after a <c>%</c> too, so <c>%PATH%</c> names no variable.
    /// </summary>
    [Theory]
    [InlineData("crew.yaml", "crew.yaml")]
    [InlineData(@"C:\crews\crew.yaml", @"C:\crews\crew.yaml")]
    [InlineData(@"C:\my crews\crew.yaml", "\"C:\\my crews\\crew.yaml\"")]
    [InlineData("{\"topic\":\"rag\"}", "^\"{\\^\"topic\\^\":\\^\"rag\\^\"}^\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    [InlineData("", "\"\"")]
    [InlineData("R&D", "\"R&D\"")]
    [InlineData("a^b", "\"a^b\"")]
    [InlineData("10%", "^\"10%^\"")]
    [InlineData("%PATH%", "^\"%^PATH%^\"")]
    public void Windows_quoting_writes_each_argument_for_the_cmd_prompt(string argument, string expected)
    {
        Assert.Equal(expected, CommandLineDisplay.Quote(argument, CommandLineQuotingStyle.Windows));
    }

    /// <summary>
    /// STUDIO-51, decision 9: « Copy the command », pasted into <c>cmd</c>, launches what Studio
    /// launches — read through a model of the prompt (<c>%%</c> stays two characters, a defined
    /// <c>%NAME%</c> is replaced) then of the C runtime. A line break is a space on that line; the
    /// run itself receives the text as it is.
    /// </summary>
    [Fact]
    public void Pasted_at_the_cmd_prompt_the_windows_line_hands_orkeon_what_studio_passes()
    {
        string[] arguments =
        [
            "run", @"C:\Users\Zoé\R&D\crew.yaml", "--var", "x=R&D 10% ^x %PATH%", "seuil=10%",
            "--initial-context=dit \"oui\" & part", "a^b", "ligne 1\nligne 2", @"C:\dossier\",
        ];

        var line = CommandLineDisplay.Format(arguments, style: CommandLineQuotingStyle.Windows);
        var argv = CrtArgv.Split(CmdBatchModel.PromptLine(line, new Dictionary<string, string> { ["PATH"] = @"C:\Windows" }));

        Assert.Equal(["orkeon", .. arguments.Select(HostileLauncherInputs.CmdHandsOver)], argv);
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
