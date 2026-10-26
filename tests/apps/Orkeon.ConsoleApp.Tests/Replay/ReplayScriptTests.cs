using Orkeon.ConsoleApp.Replay;

namespace Orkeon.ConsoleApp.Tests.Replay;

/// <summary>The replay file format: LF or CRLF, blank and <c>#</c> lines skipped, numbers from the file.</summary>
public sealed class ReplayScriptTests
{
    [Fact]
    public void ShouldKeepLinesWithTheirFileNumbers_WhenBlankAndCommentLinesAreSkipped()
    {
        var lines = ReplayScript.Parse("/help\n\n   \n# a comment\n   # indented comment\nexplain the code\n");

        Assert.Equal(
            [new ReplayLine(1, "/help"), new ReplayLine(6, "explain the code")],
            lines);
    }

    [Fact]
    public void ShouldStripTheCarriageReturn_WhenLinesEndWithCrlf()
    {
        var lines = ReplayScript.Parse("/help\r\ny\r\n\r\n# done\r\n");

        Assert.Equal([new ReplayLine(1, "/help"), new ReplayLine(2, "y")], lines);
    }

    [Fact]
    public void ShouldKeepTheLineAsTyped_WhenItHasLeadingSpacesOrAHashInside()
    {
        var lines = ReplayScript.Parse("  /tool grep # not a comment");

        Assert.Equal("  /tool grep # not a comment", Assert.Single(lines).Text);
    }

    [Fact]
    public void ShouldReturnNoLine_WhenTheFileHoldsOnlyCommentsAndBlanks()
        => Assert.Empty(ReplayScript.Parse("# nothing\n\n"));
}
