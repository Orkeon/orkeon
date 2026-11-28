using Orkeon.Tools.Code.Sessions.Windows;

namespace Orkeon.Tools.Code.Tests.Sessions;

/// <summary><see cref="CapturedOutput"/>: the head and the tail of a Windows stream, and what lies between them.</summary>
public sealed class CapturedOutputTests
{
    [Fact]
    [Trait("BR", "CH-492")]
    public void ShouldKeepEverything_WhenTheStreamFitsTheHead()
    {
        var capture = new CapturedOutput(10);

        capture.Append("hello".ToCharArray(), 5);

        Assert.Equal(("hello", "", 0L), capture.Snapshot());
    }

    [Fact]
    [Trait("BR", "CH-492")]
    public void ShouldKeepTheHeadTheTailAndTheCount_WhenTheStreamIsLong()
    {
        var capture = new CapturedOutput(4);
        var text = "abcdefghijklmnopqrstuvwxyz";

        foreach (var chunk in text.Chunk(3))
            capture.Append(chunk, chunk.Length);

        var (head, tail, omitted) = capture.Snapshot();
        Assert.Equal("abcd", head);
        Assert.Equal("wxyz", tail);
        Assert.Equal(text.Length - 8L, omitted);
    }
}
