using Orkeon.Application.Crew.Execution;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>#434: a text System One reads, or a prompt carries, is cut to a number of characters, never inside a surrogate pair.</summary>
public sealed class TextCutTests
{
    // Nine letters, then an emoji whose two halves are the 10th and 11th characters, then nine more letters.
    private const string Text = "abcdefghi\U0001F600jklmnopqr";

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldStopBeforeTheEmoji_WhenTheCutFallsBetweenItsHalves()
    {
        // Arrange
        const int max = 10;

        // Act
        var head = TextCut.Head(Text, max);

        // Assert
        Assert.Equal("abcdefghi", head);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldKeepTheEmojiWhole_WhenTheCutFallsAfterIt()
    {
        // Arrange
        const int max = 11;

        // Act
        var head = TextCut.Head(Text, max, "...");

        // Assert
        Assert.Equal("abcdefghi\U0001F600...", head);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldLeaveTheTextAsItIs_WhenItFitsTheCut()
    {
        // Arrange
        var max = Text.Length;

        // Act
        var head = TextCut.Head(Text, max, "...");

        // Assert
        Assert.Equal(Text, head);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldKeepNoHalfEmojiAtEitherEnd_WhenTheMiddleIsCut()
    {
        // Arrange: six characters kept at each end, the tail's first one the second half of the emoji.
        const string text = "abcdefghijklmnopqr\U0001F600abcde";

        // Act
        var middle = TextCut.Middle(text, 14, "~");

        // Assert
        Assert.Equal("abcdef~abcde", middle);
    }
}
