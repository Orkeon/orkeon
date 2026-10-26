using Orkeon.Cli.TerminalGui.Layout;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public class TranscriptBufferTests
{
    // Work wrapped by each of `count` appends of a fixed-length line.
    private static long[] WorkPerAppend(TranscriptBuffer buffer, int count, Action<TranscriptBuffer, int> append)
    {
        var work = new long[count];
        for (var i = 0; i < count; i++)
        {
            var before = buffer.CharactersWrapped;
            append(buffer, i);
            work[i] = buffer.CharactersWrapped - before;
        }
        return work;
    }

    [Fact]
    public void ShouldWrapOnlyTheAddedLine_WhenTenThousandLinesAreAppended()
    {
        var buffer = new TranscriptBuffer(capacity: 2_000);
        buffer.SetLayout(width: 40, wordWrap: true);

        var work = WorkPerAppend(buffer, 10_000, (b, i) => b.Append($"line {i:D5} with some words to wrap past forty columns\n"));

        // Every append wraps the same characters: the cost does not grow with the transcript.
        Assert.All(work, w => Assert.Equal(work[0], w));
        Assert.Equal(work.Take(1_000).Sum(), work.TakeLast(1_000).Sum());
        Assert.Equal(2_000, buffer.LineCount);
    }

    [Fact]
    public void ShouldWrapOnlyTheAddedLines_WhenLogLinesAreAppendedPastTheCapacity()
    {
        var buffer = new TranscriptBuffer(capacity: 500);
        buffer.SetLayout(width: 30, wordWrap: true);

        var work = WorkPerAppend(buffer, 10_000, (b, i) => b.AppendLines([$"log {i:D5} entry"]));

        Assert.All(work, w => Assert.Equal(work[0], w));
        Assert.Equal(500, buffer.LineCount);
    }

    [Fact]
    public void ShouldDropTheHead_WhenTheCapacityIsExceeded()
    {
        var buffer = new TranscriptBuffer(capacity: 3);

        buffer.AppendLines(["a", "b", "c", "d", "e"]);

        Assert.Equal(3, buffer.LineCount);
        Assert.Equal("c\nd\ne", buffer.Text);
        Assert.Equal(2, buffer.FirstLineId);
    }

    [Fact]
    public void ShouldExtendTheOpenLine_WhenTextHasNoNewline()
    {
        var buffer = new TranscriptBuffer(capacity: 10);

        buffer.Append("ab");
        buffer.Append("cd\nef");
        buffer.Append("\n");

        Assert.Equal("abcd\nef\n", buffer.Text);
        Assert.Equal(3, buffer.LineCount);
    }

    [Fact]
    public void ShouldWrapAtTheLastSpace_WhenALineExceedsTheWidth()
    {
        var buffer = new TranscriptBuffer(capacity: 10);
        buffer.SetLayout(width: 10, wordWrap: true);

        buffer.Append("hello big world");

        Assert.Equal(2, buffer.RowCount);
        var first = buffer.GetRow(0)!.Value;
        var second = buffer.GetRow(1)!.Value;
        Assert.Equal("hello big ", first.Text[first.Start..first.End]);
        Assert.Equal("world", second.Text[second.Start..second.End]);
        // The wrap is display-only: the text keeps no injected break.
        Assert.Equal("hello big world", buffer.Text);
    }

    [Fact]
    public void ShouldHardBreak_WhenAWordIsWiderThanTheRow()
    {
        var buffer = new TranscriptBuffer(capacity: 10);
        buffer.SetLayout(width: 4, wordWrap: true);

        buffer.Append("abcdefghij");

        Assert.Equal(3, buffer.RowCount);
    }

    [Fact]
    public void ShouldKeepOneRowPerLine_WhenTheWrapIsOff()
    {
        var buffer = new TranscriptBuffer(capacity: 10);
        buffer.SetLayout(width: 4, wordWrap: false);

        buffer.AppendLines(["abcdefghij", "xy"]);

        Assert.Equal(2, buffer.RowCount);
        Assert.Equal(10, buffer.MaxColumns);
    }

    [Fact]
    public void ShouldRewrapEveryLineOnce_WhenTheWidthChanges()
    {
        var buffer = new TranscriptBuffer(capacity: 100);
        buffer.SetLayout(width: 80, wordWrap: true);
        buffer.AppendLines(Enumerable.Range(0, 10).Select(i => $"entry {i} has a few words"));
        var before = buffer.CharactersWrapped;

        buffer.SetLayout(width: 80, wordWrap: true);
        Assert.Equal(before, buffer.CharactersWrapped);

        buffer.SetLayout(width: 10, wordWrap: true);
        Assert.Equal(before * 2, buffer.CharactersWrapped);
        Assert.True(buffer.RowCount > 10);
    }

    [Fact]
    public void ShouldReturnTheSourceText_WhenARangeSpansWrappedRows()
    {
        var buffer = new TranscriptBuffer(capacity: 10);
        buffer.SetLayout(width: 6, wordWrap: true);
        buffer.AppendLines(["one two three", "four"]);

        var start = buffer.HitTest(row: 0, column: 4);
        var text = buffer.GetText(start, buffer.End);

        Assert.Equal("two three\nfour", text);
        Assert.Equal(text, buffer.GetText(buffer.End, start));
    }

    [Fact]
    public void ShouldFindTheRowOfEachLine_WhenTheHeadWasTrimmed()
    {
        var buffer = new TranscriptBuffer(capacity: 4);
        buffer.SetLayout(width: 5, wordWrap: true);
        buffer.AppendLines(["aaaa bbbb", "c", "dddd eeee", "f", "g"]);

        // "aaaa bbbb" was dropped: c | dddd, eeee | f | g
        Assert.Equal(5, buffer.RowCount);
        var row = buffer.GetRow(2)!.Value;
        Assert.Equal("eeee", row.Text[row.Start..row.End]);
        Assert.Equal(buffer.FirstLineId + 1, row.Line);
        Assert.Null(buffer.GetRow(5));
    }

    [Fact]
    public void ShouldEmptyTheModel_WhenCleared()
    {
        var buffer = new TranscriptBuffer(capacity: 10);
        buffer.Append("a\nb\n");

        buffer.Clear();

        Assert.Equal(0, buffer.LineCount);
        Assert.Equal(0, buffer.RowCount);
        Assert.Equal(string.Empty, buffer.Text);
    }
}
