using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class LlmResponseFormatTests
{
    [Fact]
    public void ShouldDefaultTypeToText_WhenCreatedWithoutValue()
    {
        var fmt = new LlmResponseFormat();
        Assert.Equal("text", fmt.Type);
    }

    [Fact]
    public void ShouldExposeJsonObject_WhenUsingFactory()
    {
        var fmt = LlmResponseFormat.JsonObject();
        Assert.Equal("json_object", fmt.Type);
    }

    [Fact]
    public void ShouldExposeText_WhenUsingFactory()
    {
        var fmt = LlmResponseFormat.Text();
        Assert.Equal("text", fmt.Type);
    }

    [Fact]
    public void ShouldBeEqualByValue_WhenTypesMatch()
    {
        var a = LlmResponseFormat.JsonObject();
        var b = new LlmResponseFormat { Type = "json_object" };
        Assert.Equal(a, b);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenTypesDiffer()
    {
        Assert.NotEqual(LlmResponseFormat.JsonObject(), LlmResponseFormat.Text());
    }

    [Fact]
    public void ShouldSupportWithExpression_WhenChangingType()
    {
        var fmt = LlmResponseFormat.Text() with { Type = "json_object" };
        Assert.Equal("json_object", fmt.Type);
    }
}
