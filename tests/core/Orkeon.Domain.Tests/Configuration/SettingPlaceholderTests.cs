using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.Configuration;

/// <summary>A setting value is a variable reference only in the forms the runtime expands.</summary>
public class SettingPlaceholderTests
{
    [Theory]
    [Trait("BR", "CH-14")]
    [InlineData("${X}", "X")]
    [InlineData(" ${X} ", "X")]
    [InlineData("${env:X}", "X")]
    [InlineData("${X:-d}", "X")]
    [InlineData("${}", "")]
    [InlineData("pre${X}post", "X")]
    [InlineData("${input:github_token}", null)]
    [InlineData("${X:?e}", "X")]
    [InlineData("${X:=d}", "X")]
    [InlineData("${X:+alt}", "X")]
    [InlineData("${config:x}", null)]
    [InlineData("pre${input:k}post", null)]
    [InlineData("${X-d}", null)]
    [InlineData("${X?e}", null)]
    [InlineData("${OPENAI_API_KEY?missing}", null)]
    [InlineData("${X+alt}", null)]
    [InlineData("%X%", "X")]
    [InlineData("%ProgramFiles(x86)%", "ProgramFiles(x86)")]
    public void ShouldRecogniseAReference_WhenTheValueHoldsOne(string value, string? expectedName)
    {
        var matched = SettingPlaceholder.TryMatch(value, out var name);

        Assert.True(matched);
        Assert.Equal(expectedName, name);
    }

    [Theory]
    [Trait("BR", "CH-15")]
    [InlineData("$X")]
    [InlineData("$env:X")]
    [InlineData("%d items")]
    [InlineData("50%")]
    [InlineData("{X}")]
    [InlineData("https://h/%C3%A9t%C3%A9")]
    [InlineData("")]
    [InlineData(null)]
    public void ShouldNotRecogniseAReference_WhenTheValueOnlyLooksLikeOne(string? value)
    {
        var matched = SettingPlaceholder.TryMatch(value, out var name);

        Assert.False(matched);
        Assert.Null(name);
    }
}
