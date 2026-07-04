using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class LlmConfigOverrideTests
{
    [Fact]
    public void ShouldDefaultAllFieldsToNull_WhenCreatedEmpty()
    {
        var ov = new LlmConfigOverride();
        Assert.Null(ov.ResponseFormat);
        Assert.Null(ov.Temperature);
        Assert.Null(ov.MaxTokens);
        Assert.Null(ov.TopP);
        Assert.Null(ov.Thinking);
    }

    [Fact]
    public void ShouldExposeResponseFormat_WhenUsingForResponseFormatFactory()
    {
        var ov = LlmConfigOverride.ForResponseFormat(LlmResponseFormat.JsonObject());
        Assert.NotNull(ov.ResponseFormat);
        Assert.Equal("json_object", ov.ResponseFormat!.Type);
        Assert.Null(ov.Temperature);
    }

    [Fact]
    public void ShouldBeEqualByValue_WhenFieldsMatch()
    {
        var a = new LlmConfigOverride { ResponseFormat = LlmResponseFormat.JsonObject(), Temperature = 0.2 };
        var b = new LlmConfigOverride { ResponseFormat = LlmResponseFormat.JsonObject(), Temperature = 0.2 };
        Assert.Equal(a, b);
    }

    [Fact]
    public void ShouldNotBeEqual_WhenAnyFieldDiffers()
    {
        var a = new LlmConfigOverride { Temperature = 0.2 };
        var b = new LlmConfigOverride { Temperature = 0.3 };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ShouldSupportWithExpression_PreservingImmutability()
    {
        var a = LlmConfigOverride.ForResponseFormat(LlmResponseFormat.Text());
        var b = a with { Temperature = 0.5 };
        Assert.Null(a.Temperature);
        Assert.Equal(0.5, b.Temperature);
    }
}
