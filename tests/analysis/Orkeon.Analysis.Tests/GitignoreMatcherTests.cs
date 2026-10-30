using Orkeon.Analysis.Core;

namespace Orkeon.Analysis.Tests;

public class GitignoreMatcherTests
{
    [Theory]
    [InlineData("src/apps/Orkeon.Studio.Wpf/obj-linux", true)]
    [InlineData("src/apps/Orkeon.Studio.Wpf/Obj-Linux", true)]
    [InlineData("src/apps/Orkeon.Studio.Wpf/xbj-linux", false)]
    public void ShouldMatchACharacterClass_WhenThePatternHoldsOne(string path, bool ignored)
    {
        var matcher = new GitignoreMatcher(["[Oo]bj-[Ll]inux/"]);

        Assert.Equal(ignored, matcher.IsIgnored(path, isDirectory: true));
    }

    [Theory]
    [InlineData("log7.txt", true)]
    [InlineData("logx.txt", false)]
    public void ShouldMatchARange_WhenTheClassHoldsOne(string path, bool ignored)
    {
        var matcher = new GitignoreMatcher(["log[0-9].txt"]);

        Assert.Equal(ignored, matcher.IsIgnored(path, isDirectory: false));
    }

    [Theory]
    [InlineData("[!a]", "b.txt", true)]
    [InlineData("[!a]", "a.txt", false)]
    [InlineData("[^a]", "b.txt", true)]
    [InlineData("[^a]", "a.txt", false)]
    public void ShouldExcludeTheMembers_WhenTheClassIsNegated(string cls, string path, bool ignored)
    {
        var matcher = new GitignoreMatcher([cls + ".txt"]);

        Assert.Equal(ignored, matcher.IsIgnored(path, isDirectory: false));
    }

    [Fact]
    public void ShouldNotMatchASlash_WhenTheClassIsNegated()
    {
        var matcher = new GitignoreMatcher(["a[!x]b"]);

        Assert.False(matcher.IsIgnored("a/b", isDirectory: false));
    }

    [Fact]
    public void ShouldMatchALiteralBracket_WhenItIsEscaped()
    {
        var matcher = new GitignoreMatcher([@"\[draft\].md"]);

        Assert.True(matcher.IsIgnored("[draft].md", isDirectory: false));
        Assert.False(matcher.IsIgnored("d.md", isDirectory: false));
    }

    [Fact]
    public void ShouldMatchALiteralBracket_WhenTheClassIsNotClosed()
    {
        var matcher = new GitignoreMatcher(["notes[.md"]);

        Assert.True(matcher.IsIgnored("notes[.md", isDirectory: false));
    }

    [Fact]
    public void ShouldTakeAClosingBracketAsAMember_WhenItOpensTheClass()
    {
        var matcher = new GitignoreMatcher(["x[]a].txt"]);

        Assert.True(matcher.IsIgnored("x].txt", isDirectory: false));
        Assert.True(matcher.IsIgnored("xa.txt", isDirectory: false));
        Assert.False(matcher.IsIgnored("xb.txt", isDirectory: false));
    }
}
