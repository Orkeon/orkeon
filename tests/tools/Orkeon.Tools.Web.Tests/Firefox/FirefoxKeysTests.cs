using Orkeon.Tools.Web.Firefox;

namespace Orkeon.Tools.Web.Tests.Firefox;

public class FirefoxKeysTests
{
    [Theory]
    [InlineData("Enter", new[] { "" })]
    [InlineData("enter", new[] { "" })]
    [InlineData("Escape", new[] { "" })]
    [InlineData("ArrowDown", new[] { "" })]
    [InlineData("F1", new[] { "" })]
    [InlineData("F12", new[] { "" })]
    [InlineData("Space", new[] { " " })]
    [InlineData("a", new[] { "a" })]
    [InlineData("é", new[] { "é" })]
    [InlineData("+", new[] { "+" })]
    [InlineData("Control+a", new[] { "", "a" })]
    [InlineData("ctrl+Shift+Tab", new[] { "", "", "" })]
    [InlineData("Control++", new[] { "", "+" })]
    [InlineData("Meta+ArrowLeft", new[] { "", "" })]
    public void Should_ReadAKeyOrAChord(string key, string[] expected)
    {
        Assert.True(FirefoxKeys.TryParse(key, out var chord));
        Assert.Equal(expected, chord);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Entr")]
    [InlineData("ab")]
    [InlineData("Hyper+a")]
    [InlineData("a+Control")]
    public void Should_RefuseWhatIsNotAKey(string? key)
    {
        Assert.False(FirefoxKeys.TryParse(key, out var chord));
        Assert.Null(chord);
    }

    [Fact]
    public void Should_TypeOneKeyPerCharacter_WithEnterForALineFeed()
    {
        Assert.Equal(["a", "é", "\U0001F600", "", "", "b"], FirefoxKeys.Typing("aé\U0001F600\r\n\tb"));
    }
}
