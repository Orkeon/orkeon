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

    [Fact]
    [Trait("BR", "R-05")]
    public void ShouldMatchRootEntry_WhenPatternStartsWithDoubleStarSlash()
    {
        var matcher = new GitignoreMatcher(["**/foo"]);

        Assert.True(matcher.IsIgnored("foo", isDirectory: false));
        Assert.True(matcher.IsIgnored("a/b/foo", isDirectory: false));
        Assert.False(matcher.IsIgnored("xfoo", isDirectory: false));
    }

    [Fact]
    [Trait("BR", "R-05")]
    public void ShouldMatchZeroDirectories_WhenDoubleStarIsInTheMiddle()
    {
        var matcher = new GitignoreMatcher(["a/**/b"]);

        Assert.True(matcher.IsIgnored("a/b", isDirectory: false));
        Assert.True(matcher.IsIgnored("a/x/y/b", isDirectory: false));
    }

    [Fact]
    [Trait("BR", "R-05")]
    public void ShouldMatchEverythingInside_WhenPatternEndsWithSlashDoubleStar()
    {
        var matcher = new GitignoreMatcher(["abc/**"]);

        Assert.True(matcher.IsIgnored("abc/x", isDirectory: false));
        Assert.True(matcher.IsIgnored("abc/x/y", isDirectory: false));
        Assert.False(matcher.IsIgnored("sub/abc/x", isDirectory: false));
    }

    [Fact]
    [Trait("BR", "R-06")]
    public void ShouldAnchorPattern_WhenSlashIsInTheMiddle()
    {
        var matcher = new GitignoreMatcher(["docs/build"]);

        Assert.True(matcher.IsIgnored("docs/build", isDirectory: true));
        Assert.False(matcher.IsIgnored("tools/docs/build", isDirectory: true));
    }

    [Fact]
    [Trait("BR", "R-38")]
    public void ShouldKeepTrailingSpace_WhenEscaped()
    {
        var trailing = new GitignoreMatcher([@"foo\ "]);
        var leading = new GitignoreMatcher(["  bar"]);

        Assert.True(trailing.IsIgnored("foo ", isDirectory: false));
        Assert.False(trailing.IsIgnored("foo", isDirectory: false));
        Assert.True(leading.IsIgnored("  bar", isDirectory: false));
        Assert.False(leading.IsIgnored("bar", isDirectory: false));
    }
}
