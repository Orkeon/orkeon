using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class LlmConfigResolverTests
{
    private static LlmConfig MakeBase() => LlmConfig.OnProfile() with
    {
        Temperature = 0.5,
        MaxTokens = 2048,
        TopP = 0.9,
        Thinking = null,
        ResponseFormat = null,
    };

    [Fact]
    [Trait("BR", "CH-08")]
    public void ShouldReturnBaseConfig_WhenAllOverridesAreNull()
    {
        var baseCfg = MakeBase();
        var result = LlmConfigResolver.Resolve(baseCfg, null);

        Assert.Equal(baseCfg.Temperature, result.Temperature);
        Assert.Equal(baseCfg.MaxTokens, result.MaxTokens);
        Assert.Equal(baseCfg.TopP, result.TopP);
        Assert.Null(result.ResponseFormat);
        Assert.Null(result.Thinking);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void ShouldApplyTaskOverride_WhenCallOverrideIsNull()
    {
        var baseCfg = MakeBase();
        var task = new LlmConfigOverride { ResponseFormat = LlmResponseFormat.JsonObject(), Temperature = 0.1 };

        var result = LlmConfigResolver.Resolve(baseCfg, task);

        Assert.Equal("json_object", result.ResponseFormat!.Type);
        Assert.Equal(0.1, result.Temperature);
        Assert.Equal(baseCfg.MaxTokens, result.MaxTokens);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void ShouldKeepBaseValue_WhenOverrideFieldIsNullButOtherSet()
    {
        var baseCfg = MakeBase() with { Temperature = 0.7 };
        var task = new LlmConfigOverride { ResponseFormat = LlmResponseFormat.JsonObject() }; // Temperature null

        var result = LlmConfigResolver.Resolve(baseCfg, task);

        Assert.Equal(0.7, result.Temperature); // base preserved
        Assert.Equal("json_object", result.ResponseFormat!.Type);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void ShouldCascadeTheProfile_TaskOverBase()
    {
        // GAP-17: the host profile follows the same cascade as every other field.
        var baseCfg = LlmConfig.OnProfile("claude");

        Assert.Equal("claude", LlmConfigResolver.Resolve(baseCfg, null).Profile);
        Assert.Equal("local", LlmConfigResolver.Resolve(baseCfg, new LlmConfigOverride { Profile = "local" }).Profile);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void OnProfile_PinsNoModel_AndBlankMeansTheDefaultProfile()
    {
        var onProfile = LlmConfig.OnProfile("claude");

        Assert.Equal(string.Empty, onProfile.Model);
        Assert.Equal("claude", onProfile.Profile);
        Assert.Null(LlmConfig.OnProfile("  ").Profile);
        Assert.Null(LlmConfig.OnProfile().Profile);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void ShouldThrow_WhenBaseConfigIsNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => LlmConfigResolver.Resolve(null!, null));
    }
}
