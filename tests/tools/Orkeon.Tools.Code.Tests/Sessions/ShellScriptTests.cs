using System.Diagnostics;
using Orkeon.Tools.Code.Sessions;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary>
/// <see cref="ShellScript"/>: the quoting that keeps the shell from expanding anything an agent wrote,
/// the fragment of a command, and the reading of its framed answer.
/// </summary>
public sealed class ShellScriptTests
{
    private const string Nonce = "0123456789abcdef0123456789abcdef";

    public static TheoryData<string, string> QuotedWords => new()
    {
        { "plain", "'plain'" },
        { "", "''" },
        { "two words", "'two words'" },
        { "it's", "'it'\\''s'" },
        { "''", "''\\'''\\'''" },
        { "$HOME", "'$HOME'" },
        { "`id`", "'`id`'" },
        { "$(id)", "'$(id)'" },
        { "line1\nline2", "'line1\nline2'" },
        { "a\\b", "'a\\b'" },
        { "*", "'*'" },
    };

    [Theory]
    [MemberData(nameof(QuotedWords))]
    public void Quote_ShouldWrapTheWordInSingleQuotes_WithEachSingleQuoteClosedEscapedAndReopened(string word, string expected)
    {
        Assert.Equal(expected, ShellScript.Quote(word));
    }

    [Fact]
    public void Quote_ShouldRefuseANulCharacter()
    {
        Assert.Throws<ArgumentException>(() => ShellScript.Quote("a\0b"));
    }

    /// <summary>The shell itself reads each quoted word back as exactly the word.</summary>
    [Fact]
    public async Task Quote_ShouldBeReadBackVerbatimByTheShell()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "No POSIX shell on Windows.");
        string[] words = ["it's", "", "two words", "$HOME", "`id`", "$(id)", "line1\nline2", "a\\b", "*", "'\"'", "-n"];
        var script = "printf '%s\\0' " + string.Join(' ', words.Select(ShellScript.Quote));
        var startInfo = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, UseShellExecute = false };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(script);

        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(words, output.Split('\0')[..^1]);
    }

    [Fact]
    public void Command_ShouldQuoteTheDirectoryTheExecutableAndEachArgument()
    {
        var fragment = ShellScript.Command(
            new ShellInvocation("grep", ["-r", "it's here", "/work space"], "/work space", TimeSpan.FromSeconds(5)), Nonce);

        Assert.Contains(
            "( cd '/work space' && exec 'grep' '-r' 'it'\\''s here' '/work space' ) </dev/null >\"$_ork_o\" 2>\"$_ork_e\"; _ork_rc=$?",
            fragment,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Command_ShouldKeepTheSessionDirectory_WhenNoWorkingDirectoryIsGiven()
    {
        var fragment = ShellScript.Command(new ShellInvocation("pwd", [], null, TimeSpan.FromSeconds(5)), Nonce);

        Assert.Contains("( exec 'pwd' ) </dev/null", fragment, StringComparison.Ordinal);
        Assert.DoesNotContain("cd ", fragment, StringComparison.Ordinal);
    }

    [Fact]
    public void Command_ShouldPrintBothStreamsCappedAndTheExitCode_FramedByTheNonce()
    {
        var fragment = ShellScript.Command(new ShellInvocation("pwd", [], null, TimeSpan.FromSeconds(5)), Nonce, byteCap: 42);

        Assert.Contains($"printf '\\n%s:OUT\\n' '{Nonce}'; head -c 42 \"$_ork_o\"", fragment, StringComparison.Ordinal);
        Assert.Contains($"printf '\\n%s:ERR\\n' '{Nonce}'; head -c 42 \"$_ork_e\"", fragment, StringComparison.Ordinal);
        Assert.Contains($"printf '\\n%s:RC:%s\\n' '{Nonce}' \"$_ork_rc\"", fragment, StringComparison.Ordinal);
        Assert.EndsWith("\n", fragment, StringComparison.Ordinal);
    }

    [Fact]
    public void Command_ShouldCapEachStreamAboveWhatTheToolKeeps()
    {
        // Three bytes per UTF-16 character at most: the cut stream still holds more than the tool keeps.
        Assert.True(ShellScript.ByteCap / 3 > ShellCommandTool.MaxOutputLength);
    }

    [Fact]
    public void Command_ShouldRefuseAnArgumentHoldingANulCharacter()
    {
        Assert.Throws<ArgumentException>(() =>
            ShellScript.Command(new ShellInvocation("echo", ["a\0b"], null, TimeSpan.FromSeconds(5)), Nonce));
    }

    [Fact]
    public void Preamble_ShouldMakeThePrivateDirectoryAndPrintTheReadyMarker()
    {
        var preamble = ShellScript.Preamble(Nonce, watchedHostPid: null);

        Assert.StartsWith("_ork_d=$(mktemp -d 2>/dev/null) ||", preamble, StringComparison.Ordinal);
        Assert.Contains("trap 'rm -rf \"$_ork_d\"' EXIT", preamble, StringComparison.Ordinal);
        Assert.EndsWith($"printf '\\n%s:READY:%s\\n' '{Nonce}' \"$_ork_d\"\n", preamble, StringComparison.Ordinal);
        Assert.DoesNotContain(ShellScript.WatchdogName, preamble, StringComparison.Ordinal);
    }

    [Fact]
    public void Preamble_ShouldStartTheWatchdogOnTheHostPid_AndStopItWhenTheShellExits()
    {
        var preamble = ShellScript.Preamble(Nonce, watchedHostPid: 4242);

        Assert.Contains($"' {ShellScript.WatchdogName} 4242 $$ \"$_ork_d\" </dev/null >/dev/null 2>&1 &\n_ork_w=$!\n", preamble, StringComparison.Ordinal);
        Assert.Contains("/bin/sh -c " + ShellScript.Quote(ShellScript.WatchdogScript), preamble, StringComparison.Ordinal);
        Assert.Contains("trap 'kill \"$_ork_w\" 2>/dev/null; rm -rf \"$_ork_d\"' EXIT", preamble, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseFrame_ShouldReadBothStreamsAndTheExitCode_WhenTheFrameIsWhole()
    {
        var buffer = $"\n{Nonce}:OUT\nhello\nworld\n{Nonce}:ERR\noops\n{Nonce}:RC:3\nrest";

        var status = ShellScript.ParseFrame(buffer, Nonce, out var frame, out var consumed);

        Assert.Equal(ShellFrameStatus.Complete, status);
        Assert.Equal(new ShellFrame("hello\nworld", "oops", 3), frame);
        Assert.Equal("rest", buffer[consumed..]);
    }

    [Fact]
    public void ParseFrame_ShouldKeepTheStreamsExactly_WhenTheyAreEmptyOrEndWithANewline()
    {
        var empty = ShellScript.ParseFrame($"\n{Nonce}:OUT\n\n{Nonce}:ERR\n\n{Nonce}:RC:0\n", Nonce, out var emptyFrame, out _);
        var newline = ShellScript.ParseFrame($"\n{Nonce}:OUT\na\n\n{Nonce}:ERR\n\n\n{Nonce}:RC:0\n", Nonce, out var newlineFrame, out _);

        Assert.Equal(ShellFrameStatus.Complete, empty);
        Assert.Equal(new ShellFrame("", "", 0), emptyFrame);
        Assert.Equal(ShellFrameStatus.Complete, newline);
        Assert.Equal(new ShellFrame("a\n", "\n", 0), newlineFrame);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n0123")]
    [InlineData("\n0123456789abcdef0123456789abcdef:OUT\nhalf of the out")]
    [InlineData("\n0123456789abcdef0123456789abcdef:OUT\nx\n0123456789abcdef0123456789abcdef:ERR\n")]
    [InlineData("\n0123456789abcdef0123456789abcdef:OUT\nx\n0123456789abcdef0123456789abcdef:ERR\ny\n0123456789abcdef0123456789abcdef:RC:1")]
    public void ParseFrame_ShouldAskForMore_UntilTheExitCodeLineIsWhole(string buffer)
    {
        Assert.Equal(ShellFrameStatus.Incomplete, ShellScript.ParseFrame(buffer, Nonce, out var frame, out var consumed));
        Assert.Null(frame);
        Assert.Equal(0, consumed);
    }

    [Theory]
    [InlineData("garbage before the frame")]
    [InlineData("\nffffffffffffffffffffffffffffffff:OUT\nx\nffffffffffffffffffffffffffffffff:ERR\n\nffffffffffffffffffffffffffffffff:RC:0\n")]
    [InlineData("\n0123456789abcdef0123456789abcdef:OUT\nno error marker\n0123456789abcdef0123456789abcdef:RC:0\n")]
    [InlineData("\n0123456789abcdef0123456789abcdef:OUT\n\n0123456789abcdef0123456789abcdef:ERR\n\n0123456789abcdef0123456789abcdef:RC:zero\n")]
    public void ParseFrame_ShouldReportTheStreamOutOfStep_WhenAMarkerIsMissingOrWrong(string buffer)
    {
        Assert.Equal(ShellFrameStatus.Malformed, ShellScript.ParseFrame(buffer, Nonce, out _, out _));
    }

    [Fact]
    public void ParseFrame_ShouldReadANegativeExitCode()
    {
        ShellScript.ParseFrame($"\n{Nonce}:OUT\n\n{Nonce}:ERR\n\n{Nonce}:RC:-1\n", Nonce, out var frame, out _);

        Assert.Equal(-1, frame!.ExitCode);
    }

    [Fact]
    public void ParseFrame_ShouldTakeMarkersOfAnotherNonceAsOutput()
    {
        const string other = "ffffffffffffffffffffffffffffffff";
        var buffer = $"\n{Nonce}:OUT\n\n{other}:ERR\n\n{other}:RC:9\n\n{Nonce}:ERR\n\n{Nonce}:RC:0\n";

        ShellScript.ParseFrame(buffer, Nonce, out var frame, out _);

        Assert.Equal($"\n{other}:ERR\n\n{other}:RC:9\n", frame!.Stdout);
        Assert.Equal(0, frame.ExitCode);
    }

    [Fact]
    public void TryFindReady_ShouldReadThePrivateDirectoryAndConsumeTheWholeLine()
    {
        var buffer = $"noise\n{Nonce}:READY:/tmp/tmp.AbC123\nafter";

        Assert.True(ShellScript.TryFindReady(buffer, Nonce, out var consumed, out var directory));
        Assert.Equal("/tmp/tmp.AbC123", directory);
        Assert.Equal("after", buffer[consumed..]);
        Assert.False(ShellScript.TryFindReady($"\n{Nonce}:READY:/tmp/tmp.Ab", Nonce, out _, out _));
        Assert.False(ShellScript.TryFindReady("\nother:READY:/tmp/x\n", Nonce, out _, out _));
    }
}
