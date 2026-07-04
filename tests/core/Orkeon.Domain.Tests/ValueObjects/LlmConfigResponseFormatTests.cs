using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class LlmConfigResponseFormatTests
{
    [Fact]
    public void ShouldDefaultResponseFormatToNull_OnDefaultConfig()
    {
        var cfg = LlmConfig.Default();
        Assert.Null(cfg.ResponseFormat);
    }

    [Fact]
    public void ShouldRoundTripResponseFormat_ViaWithExpression()
    {
        var cfg = LlmConfig.Default() with { ResponseFormat = LlmResponseFormat.JsonObject() };
        Assert.NotNull(cfg.ResponseFormat);
        Assert.Equal("json_object", cfg.ResponseFormat!.Type);
    }

    [Fact]
    public void ShouldPreserveResponseFormat_WhenOtherFieldsMutated()
    {
        var cfg = LlmConfig.Default() with
        {
            ResponseFormat = LlmResponseFormat.JsonObject(),
        };
        var updated = cfg with { Temperature = 0.1 };
        Assert.Equal("json_object", updated.ResponseFormat!.Type);
        Assert.Equal(0.1, updated.Temperature);
    }
}
