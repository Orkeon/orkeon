using Orkeon.Tests.Shared.Launchers;

namespace Orkeon.Domain.Tests.FileSystem;

/// <summary>
/// The two models the launcher suites read a <c>run.cmd</c> with (STUDIO-51 § 3): how <c>cmd</c>
/// reads a line (<see cref="CmdBatchModel"/>) and how the C runtime splits what it hands over
/// (<see cref="CrtArgv"/>). Pinned on the documented readings — the ones that broke the launchers
/// included — and, for the C runtime, held against .NET on this machine.
/// </summary>
public sealed class LauncherModelTests
{
    [Theory]
    [InlineData("a b\tc", new[] { "a", "b", "c" })]
    [InlineData("\"a b\" c", new[] { "a b", "c" })]
    [InlineData(@"a\\b c\", new[] { @"a\\b", @"c\" })]
    [InlineData("\"a\\\\\" b", new[] { "a\\", "b" })]
    [InlineData("\"a\\\"b\"", new[] { "a\"b" })]
    [InlineData("a\"b c\"d", new[] { "ab cd" })]
    [InlineData("\"a\"\"b\"", new[] { "a\"b" })]
    [InlineData("\"\" x", new[] { "", "x" })]
    [InlineData("\\\"x\\\" --opt=\"v w\"", new[] { "\"x\"", "--opt=v w" })]
    [InlineData("\"\\\\serveur\\partage\\\\\"", new[] { @"\\serveur\partage\" })]
    public void The_c_runtime_split_follows_the_documented_rules(string arguments, string[] expected)
    {
        Assert.Equal(expected, CrtArgv.SplitArguments(arguments));
    }

    /// <summary>The reference: .NET splits <c>ProcessStartInfo.Arguments</c> by the C runtime's rules on Linux too.</summary>
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("a b\tc")]
    [InlineData("\"a b\" c")]
    [InlineData(@"a\\b c\")]
    [InlineData("\"a\\\\\" b")]
    [InlineData("\"a\\\"b\"")]
    [InlineData("a\"b c\"d")]
    [InlineData("\"a\"\"b\"")]
    [InlineData("\"\" x")]
    [InlineData("\\\"x\\\" --opt=\"v w\"")]
    [InlineData("\"\\\\serveur\\partage\\\\\"")]
    [InlineData("\"titre=dit \\\"oui\\\" & part\" \"seuil=10%\" \"Économie 日本語 🚀\"")]
    public void The_model_splits_as_dotnet_does(string arguments)
    {
        Assert.SkipUnless(PosixLauncherShell.IsAvailable, "the reference needs /bin/sh.");

        Assert.Equal(PosixLauncherShell.DotnetSplit(arguments), CrtArgv.SplitArguments(arguments));
    }

    [Fact]
    public void The_program_name_takes_quotes_and_no_escape()
    {
        Assert.Equal([@"C:\a b\orkeon.exe", "x"], CrtArgv.Split("\"C:\\a b\\orkeon.exe\" x"));
        Assert.Equal(["orkeon"], CrtArgv.Split("orkeon"));
    }

    [Fact]
    public void A_batch_line_reads_its_percents_before_its_quotes()
    {
        var variables = new Dictionary<string, string> { ["Path"] = @"C:\Windows" };

        Assert.Equal("orkeon \"10%\" C:\\t\\crew \"C:\\Windows\" \"\"", CmdBatchModel.BatchLine("orkeon \"10%%\" %~dp0crew \"%PATH%\" \"%NOPE%\"", @"C:\t", variables));
        Assert.Throws<CmdModelException>(() => CmdBatchModel.BatchLine("orkeon \"10%\"", @"C:\t"));
    }

    [Fact]
    public void Outside_quotes_an_operator_or_a_parenthesis_breaks_the_command_and_a_caret_escapes()
    {
        Assert.Equal("orkeon \"a & b\"", CmdBatchModel.BatchLine("orkeon \"a & b\"", @"C:\t"));
        Assert.Equal("orkeon \"a & b\"", CmdBatchModel.BatchLine("orkeon ^\"a ^& b^\"", @"C:\t"));
        Assert.Throws<CmdModelException>(() => CmdBatchModel.BatchLine("orkeon a & b", @"C:\t"));
        Assert.Throws<CmdModelException>(() => CmdBatchModel.BatchLine("orkeon a)", @"C:\t"));
        Assert.Throws<CmdModelException>(() => CmdBatchModel.BatchLine("orkeon a^", @"C:\t"));
        // A caret inside quotes stays: cmd hands it over.
        Assert.Equal("orkeon \"a^b\"", CmdBatchModel.BatchLine("orkeon \"a^b\"", @"C:\t"));
    }

    /// <summary>
    /// Reading 5, the bug the old mount token carried: <c>\"</c> is a literal quote for the C
    /// runtime but leaves <c>cmd</c>'s quotes, so <c>%~dp0</c> landed outside them and a folder with
    /// <c>&amp;</c> cut the command.
    /// </summary>
    [Fact]
    public void The_former_mount_token_puts_the_launcher_folder_outside_cmds_quotes()
    {
        const string Former = "orkeon run --mount \"\\\"%~dp0output\\\":/output:rw\"";

        Assert.Equal("orkeon run --mount \"\\\"C:\\t\\output\\\":/output:rw\"", CmdBatchModel.BatchLine(Former, @"C:\t"));
        Assert.Throws<CmdModelException>(() => CmdBatchModel.BatchLine(Former, @"C:\R&D\"));
    }

    /// <summary>
    /// Reading 7: a defined variable is replaced, <c>%%</c> and a lone <c>%</c> stay — and
    /// <c>%^PATH%</c> names no variable, so the caret is still there when the special characters
    /// are read, and goes: <c>%PATH%</c> reaches the program.
    /// </summary>
    [Fact]
    public void A_typed_line_keeps_what_names_no_variable()
    {
        var variables = new Dictionary<string, string> { ["PATH"] = @"C:\Windows" };

        Assert.Equal(@"orkeon C:\Windows %% 10% %PATH%", CmdBatchModel.PromptLine("orkeon %PATH% %% 10% %^PATH%", variables));
    }

    /// <summary>Reading 6: with <c>/s</c>, the first and the last quote go, and the path keeps the two others.</summary>
    [Fact]
    public void Cmd_slash_s_slash_c_runs_the_path_between_the_inner_quotes()
    {
        Assert.Equal(
            @"C:\Users\Zoé\R&D 100% (x)!\équipe\run.cmd",
            CmdBatchModel.SlashCProgram("/d /v:off /s /c \"\"C:\\Users\\Zoé\\R&D 100% (x)!\\équipe\\run.cmd\"\""));
        Assert.Throws<CmdModelException>(() => CmdBatchModel.SlashCProgram("/c \"C:\\R&D\\run.cmd\""));
        Assert.Throws<CmdModelException>(() => CmdBatchModel.SlashCProgram("/d /s /c \"\"C:\\a!b\\run.cmd\"\""));
    }
}
