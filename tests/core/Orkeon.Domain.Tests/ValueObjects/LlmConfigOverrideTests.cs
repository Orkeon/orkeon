using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class LlmConfigOverrideTests
{
    [Fact]
    [Trait("BR", "CH-08")]
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
    [Trait("BR", "CH-08")]
    public void ShouldBeEqualByValue_WhenFieldsMatch()
    {
        var a = new LlmConfigOverride { ResponseFormat = LlmResponseFormat.JsonObject(), Temperature = 0.2 };
        var b = new LlmConfigOverride { ResponseFormat = LlmResponseFormat.JsonObject(), Temperature = 0.2 };
        Assert.Equal(a, b);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void ShouldNotBeEqual_WhenAnyFieldDiffers()
    {
        var a = new LlmConfigOverride { Temperature = 0.2 };
        var b = new LlmConfigOverride { Temperature = 0.3 };
        Assert.NotEqual(a, b);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void ShouldSupportWithExpression_PreservingImmutability()
    {
        var a = new LlmConfigOverride { ResponseFormat = LlmResponseFormat.Text() };
        var b = a with { Temperature = 0.5 };
        Assert.Null(a.Temperature);
        Assert.Equal(0.5, b.Temperature);
    }
}
