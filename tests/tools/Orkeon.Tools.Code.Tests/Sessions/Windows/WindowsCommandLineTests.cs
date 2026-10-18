using System.Text;
using Orkeon.Tools.Code.Sessions.Windows;

namespace Orkeon.Tools.Code.Tests.Sessions.Windows;

/// <summary>
/// <see cref="WindowsCommandLine"/>: the C runtime quoting read back unchanged by a reference parser of
/// the <c>CommandLineToArgvW</c> rules, and the cmd.exe line of a built-in in which no character of an
/// argument reaches cmd's parser outside double quotes unless it is a plain word character.
/// </summary>
public sealed class WindowsCommandLineTests
{
    private const string Cmd = @"C:\Windows\System32\cmd.exe";

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("tab\there", "\"tab\there\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\dir\", @"C:\dir\")]
    [InlineData(@"C:\my dir\", "\"C:\\my dir\\\\\"")]
    [InlineData(@"a\\b", @"a\\b")]
    [InlineData("a\\\"b", "\"a\\\\\\\"b\"")]
    [InlineData(@"\\server\share", @"\\server\share")]
    public void ShouldQuoteOneArgument_AsTheCRuntimeReadsIt(string argument, string expected)
        => Assert.Equal(expected, WindowsCommandLine.Quote(argument));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a b c")]
    [InlineData("\"")]
    [InlineData("\"\"")]
    [InlineData(@"\")]
    [InlineData(@"\\")]
    [InlineData(@"trailing\")]
    [InlineData(@"trailing space\ ")]
    [InlineData("back\\\\\"slash")]
    [InlineData(@"C:\Program Files\App\")]
    [InlineData("--out=\"C:\\a b\\\\\"")]
    [InlineData("new\nline")]
    [InlineData("é ü ✓")]
    public void ShouldBeReadBackUnchanged_ByCommandLineToArgvW(string argument)
    {
        var line = WindowsCommandLine.Build(@"C:\tools\git.exe", ["first", argument, "last"]);

        Assert.Equal(["first", argument, "last"], ParseArguments(line));
    }

    [Fact]
    public void ShouldPutTheProgramBetweenQuotes()
        => Assert.Equal("\"C:\\Program Files\\Git\\cmd\\git.exe\" status", WindowsCommandLine.Build(@"C:\Program Files\Git\cmd\git.exe", ["status"]));

    [Fact]
    public void ShouldRefuseAProgramPathHoldingAQuote()
        => Assert.Throws<ArgumentException>(() => WindowsCommandLine.Build("C:\\a\"b.exe", []));

    [Fact]
    public void ShouldRunABuiltinThroughCmdWithoutAutoRunNorDelayedExpansion()
        => Assert.Equal(
            "\"" + Cmd + "\" /d /v:off /s /c \"type a.txt \"my file.txt\"\"",
            WindowsCommandLine.CmdBuiltin(Cmd, "type", ["a.txt", "my file.txt"]));

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData(@"C:\dir\file.txt", @"C:\dir\file.txt")]
    [InlineData("*.cs", "*.cs")]
    [InlineData("", "\"\"")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("a&b", "\"a&b\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("(x)", "\"(x)\"")]
    [InlineData("^", "\"^\"")]
    [InlineData("a,b;c=d", "\"a,b;c=d\"")]
    [InlineData("!PATH!", "\"!PATH!\"")]
    public void ShouldEscapeABuiltinArgument_ForCmd(string argument, string expected)
        => Assert.Equal(expected, WindowsCommandLine.CmdArgument(argument));

    [Theory]
    [InlineData("%PATH%")]
    [InlineData("50%")]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    [InlineData("a\0b")]
    public void ShouldRefuseABuiltinArgument_CmdWouldExpandOrSplit(string argument)
        => Assert.Throws<ArgumentException>(() => WindowsCommandLine.CmdBuiltin(Cmd, "echo", [argument]));

    [Theory]
    [InlineData("echo&calc")]
    [InlineData("1")]
    [InlineData("")]
    public void ShouldRefuseABuiltinThatIsNotAPlainWord(string builtin)
        => Assert.ThrowsAny<ArgumentException>(() => WindowsCommandLine.CmdBuiltin(Cmd, builtin, []));

    [Theory]
    [InlineData("a & calc.exe")]
    [InlineData("x\" & calc.exe & \"")]
    [InlineData("\" | whoami")]
    [InlineData("\"\" > out.txt")]
    [InlineData("^\" & del *")]
    [InlineData("a\"\"\"b & c")]
    [InlineData("(echo pwned)")]
    [InlineData("<in.txt")]
    [InlineData("\"")]
    [InlineData("\\\" & x")]
    public void ShouldLeaveNoCmdOperatorOutsideQuotes_WhateverTheArgumentHolds(string argument)
    {
        var line = WindowsCommandLine.CmdBuiltin(Cmd, "echo", ["safe", argument]);

        AssertNoOperatorOutsideQuotes(line);
    }

    [Fact]
    public void ShouldLeaveNoCmdOperatorOutsideQuotes_ForEveryPairOfSpecialCharacters()
    {
        const string specials = "\"&|<>^() ,;=!\\a";
        foreach (var first in specials)
        {
            foreach (var second in specials)
            {
                var argument = new string([first, second]);
                AssertNoOperatorOutsideQuotes(WindowsCommandLine.CmdBuiltin(Cmd, "echo", [argument, argument + first]));
            }
        }
    }

    /// <summary>
    /// Reads the line as cmd.exe does after <c>/s /c</c>: the first quote after <c>/c</c> and the last
    /// quote of the line go, then the quoted state toggles on every quote, and outside quotes only plain
    /// word characters and spaces may appear.
    /// </summary>
    private static void AssertNoOperatorOutsideQuotes(string line)
    {
        const string marker = " /c \"";
        var start = line.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start > 0, line);
        Assert.EndsWith("\"", line, StringComparison.Ordinal);
        var body = line[(start + marker.Length)..^1];

        var quoted = false;
        foreach (var c in body)
        {
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted)
                Assert.True(c == ' ' || char.IsAsciiLetterOrDigit(c) || "._-+/\\:~#@[]{}*?'$".Contains(c, StringComparison.Ordinal), $"'{c}' outside quotes in: {line}");
        }

        Assert.False(quoted, $"unbalanced quotes in: {line}");
    }

    /// <summary>The arguments after the program, split by the rules of <c>CommandLineToArgvW</c> (MSVC 2008 and later).</summary>
    private static List<string> ParseArguments(string line)
    {
        // The program: up to the closing quote (no escape inside it).
        Assert.StartsWith("\"", line, StringComparison.Ordinal);
        var i = line.IndexOf('"', 1) + 1;
        var arguments = new List<string>();
        while (true)
        {
            while (i < line.Length && line[i] is ' ' or '\t')
                i++;
            if (i >= line.Length)
                return arguments;

            var current = new StringBuilder();
            var inQuotes = false;
            while (i < line.Length)
            {
                var c = line[i];
                if (c == '\\')
                {
                    var backslashes = 0;
                    while (i < line.Length && line[i] == '\\')
                    {
                        backslashes++;
                        i++;
                    }

                    if (i < line.Length && line[i] == '"')
                    {
                        current.Append('\\', backslashes / 2);
                        if (backslashes % 2 == 1)
                        {
                            current.Append('"');
                            i++;
                        }
                    }
                    else
                    {
                        current.Append('\\', backslashes);
                    }

                    continue;
                }

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    i++;
                    continue;
                }

                if (c is ' ' or '\t' && !inQuotes)
                    break;

                current.Append(c);
                i++;
            }

            arguments.Add(current.ToString());
        }
    }
}
