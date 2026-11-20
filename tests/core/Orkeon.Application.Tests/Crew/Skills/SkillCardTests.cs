using Orkeon.Application.Crew.Skills;

namespace Orkeon.Application.Tests.Crew.Skills;

/// <summary>The one line the decision model reads for a skill of the library (#434).</summary>
public sealed class SkillCardTests
{
    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldReadTheFrontMatter_WhenTheFileOpensWithOne()
    {
        // Arrange
        const string head = "---\nname: Security review\ndescription: >\n  Checks a change\n  for injection.\n---\n# Ignored\nThe body.";

        // Act
        var card = SkillCard.Of("/workspace/.orkeon/skills/review.md", head);

        // Assert
        Assert.Equal("Security review: Checks a change for injection.", card.Line);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldReadTheFirstHeadingAndLine_WhenTheFileHasNoFrontMatter()
    {
        // Arrange
        const string head = "```\ncode\n```\n# Release notes\n\nWrite the notes of a release.\nMore.";

        // Act
        var card = SkillCard.Of("/workspace/.orkeon/skills/notes.md", head);

        // Assert
        Assert.Equal("Release notes: Write the notes of a release.", card.Line);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldNameTheSkillAfterItsFile_WhenTheHeadGivesNoName()
    {
        // Arrange
        const string head = "Plain text only.";

        // Act
        var card = SkillCard.Of("/workspace/.orkeon/skills/plain.md", head);

        // Assert
        Assert.Equal("plain: Plain text only.", card.Line);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldCutTheDescription_WhenItIsLongerThanItsCap()
    {
        // Arrange
        var head = "---\nname: long\ndescription: " + new string('d', 400) + "\n---\n";

        // Act
        var card = SkillCard.Of("/workspace/.orkeon/skills/long.md", head);

        // Assert
        Assert.Equal(SkillCard.DescriptionChars + 3, card.Description.Length);
    }
}
