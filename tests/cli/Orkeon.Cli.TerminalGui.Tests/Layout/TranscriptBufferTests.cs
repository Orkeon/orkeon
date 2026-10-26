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
        // The empty line opened by the final newline is not a line of the transcript.
        Assert.Equal(2, buffer.LineCount);
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

    [Fact]
    public void ShouldKeepCapacityLines_WhenTheStreamEndsWithANewline()
    {
        var buffer = new TranscriptBuffer(capacity: 3);

        for (var i = 0; i < 10; i++) buffer.Append($"line {i}\n");

        Assert.Equal(3, buffer.LineCount);
        Assert.Equal("line 7\nline 8\nline 9\n", buffer.Text);
    }

    [Fact]
    public void ShouldCountAnEmptyLine_WhenTheStreamWritesTwoNewlines()
    {
        var buffer = new TranscriptBuffer(capacity: 10);

        buffer.Append("a\n");
        buffer.Append("\n");
        buffer.Append("b");

        Assert.Equal("a\n\nb", buffer.Text);
        Assert.Equal(3, buffer.LineCount);
    }

    [Fact]
    public void ShouldCountAMultiLineEntryOnce_WhenTheCapacityIsInEntries()
    {
        var buffer = new TranscriptBuffer(capacity: 2);

        buffer.AppendLines(["first", "error\n  at A()\n  at B()", "last"]);

        // The capacity counts entries: the three-line entry is kept whole, the first entry dropped.
        Assert.Equal(2, buffer.LineCount);
        Assert.Equal("error\n  at A()\n  at B()\nlast", buffer.Text);

        buffer.AppendLines(["next"]);

        // Dropped whole as well: no stray continuation line remains at the head.
        Assert.Equal("last\nnext", buffer.Text);
    }

    [Fact]
    public void ShouldExpandTabsToTheNextStop_WhenALineIsMeasuredAndWrapped()
    {
        var buffer = new TranscriptBuffer(capacity: 10);
        buffer.SetLayout(width: 0, wordWrap: false);

        buffer.AppendLines(["A\tBC\tD"]);

        // A + 3 blanks, BC + 2 blanks, D: tab stops every four columns.
        Assert.Equal(9, buffer.MaxColumns);
        Assert.Equal(new TranscriptPosition(buffer.FirstLineId, 2), buffer.HitTest(row: 0, column: 4));
        Assert.Equal(new TranscriptPosition(buffer.FirstLineId, 1), buffer.HitTest(row: 0, column: 2));

        buffer.SetLayout(width: 6, wordWrap: true);

        // "A\tBC" fills six columns, the second tab hangs at the end of that row and "D" opens the next.
        Assert.Equal(2, buffer.RowCount);
        Assert.Equal("A\tBC\tD", buffer.Text);
    }

    [Fact]
    public void ShouldWrapOnlyTheLastRow_WhenALongLineIsStreamedInPieces()
    {
        foreach (var wrap in new[] { true, false })
        {
            var buffer = new TranscriptBuffer(capacity: 10);
            buffer.SetLayout(width: 20, wordWrap: wrap);

            var work = WorkPerAppend(buffer, 5_000, (b, _) => b.Append("tok "));

            // Each piece re-wraps at most one row plus the piece: N pieces cost O(N), not O(N^2).
            Assert.All(work, w => Assert.True(w <= 20 + 4, $"wrap={wrap}: {w} characters wrapped"));
            Assert.Equal(1, buffer.LineCount);
            Assert.Equal(work.Skip(1_000).Take(1_000).Sum(), work.TakeLast(1_000).Sum());
        }
    }

    [Fact]
    public void ShouldWrapTheStreamedLineAsAWholeLine_WhenItIsExtendedInPieces()
    {
        var streamed = new TranscriptBuffer(capacity: 10);
        var whole = new TranscriptBuffer(capacity: 10);
        streamed.SetLayout(width: 7, wordWrap: true);
        whole.SetLayout(width: 7, wordWrap: true);
        const string text = "alpha beta\tgamma delta epsilonzetaeta theta";

        foreach (var c in text) streamed.Append(c.ToString());
        whole.Append(text);

        Assert.Equal(whole.RowCount, streamed.RowCount);
        for (var row = 0; row < whole.RowCount; row++)
        {
            var a = whole.GetRow(row)!.Value;
            var b = streamed.GetRow(row)!.Value;
            Assert.Equal((a.Start, a.End), (b.Start, b.End));
        }
    }

    [Fact]
    public void ShouldShrinkTheWidestLine_WhenTheWidestLineIsTrimmed()
    {
        var buffer = new TranscriptBuffer(capacity: 2);
        buffer.SetLayout(width: 0, wordWrap: false);

        buffer.AppendLines(["a very very long line", "short"]);
        Assert.Equal(21, buffer.MaxColumns);

        buffer.AppendLines(["mid line"]);

        Assert.Equal(8, buffer.MaxColumns);
    }

    [Fact]
    public void ShouldWrapTheTextAfterATab_WhenTheTabOverflowsBeforeTheRowEnd()
    {
        var buffer = new TranscriptBuffer(capacity: 10);
        buffer.SetLayout(width: 6, wordWrap: true);

        buffer.AppendLines(["abcde\tx"]);

        // The tab is clipped to the last column of its row and "x" opens the next one.
        Assert.Equal(2, buffer.RowCount);
        var second = buffer.GetRow(1)!.Value;
        Assert.Equal("x", second.Text[second.Start..second.End]);
        Assert.Equal(new TranscriptPosition(buffer.FirstLineId, 6), buffer.HitTest(row: 1, column: 0));
        Assert.Equal(new TranscriptPosition(buffer.FirstLineId, 7), buffer.HitTest(row: 1, column: 3));
    }

    [Fact]
    public void ShouldHangATab_WhenItStartsAtTheRowEnd()
    {
        var buffer = new TranscriptBuffer(capacity: 10);
        buffer.SetLayout(width: 6, wordWrap: true);

        buffer.AppendLines(["abcdef\tx"]);

        Assert.Equal(2, buffer.RowCount);
        Assert.Equal(7, buffer.GetRow(1)!.Value.Start);
    }

    [Fact]
    public void ShouldMeasureAZwjSequenceOnce_WhenItIsStreamedInTwoPieces()
    {
        const string first = "ab\U0001F468";
        const string second = "\u200D\U0001F469\u200D\U0001F467";
        foreach (var wrap in new[] { false, true })
        {
            var streamed = new TranscriptBuffer(capacity: 10);
            var whole = new TranscriptBuffer(capacity: 10);
            streamed.SetLayout(width: wrap ? 40 : 0, wordWrap: wrap);
            whole.SetLayout(width: wrap ? 40 : 0, wordWrap: wrap);

            streamed.Append(first);
            streamed.Append(second);
            whole.Append(first + second);

            Assert.Equal(whole.MaxColumns, streamed.MaxColumns);
        }
    }
}
