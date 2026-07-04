using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

public class LlmConfigResolverTests
{
    private static LlmConfig MakeBase() => LlmConfig.Default() with
    {
        Temperature = 0.5,
        MaxTokens = 2048,
        TopP = 0.9,
        Thinking = null,
        ResponseFormat = null,
    };

    [Fact]
    public void ShouldReturnBaseConfig_WhenAllOverridesAreNull()
    {
        var baseCfg = MakeBase();
        var result = LlmConfigResolver.Resolve(baseCfg, null, null);

        Assert.Equal(baseCfg.Temperature, result.Temperature);
        Assert.Equal(baseCfg.MaxTokens, result.MaxTokens);
        Assert.Equal(baseCfg.TopP, result.TopP);
        Assert.Null(result.ResponseFormat);
        Assert.Null(result.Thinking);
    }

    [Fact]
    public void ShouldApplyTaskOverride_WhenCallOverrideIsNull()
    {
        var baseCfg = MakeBase();
        var task = new LlmConfigOverride { ResponseFormat = LlmResponseFormat.JsonObject(), Temperature = 0.1 };

        var result = LlmConfigResolver.Resolve(baseCfg, task, null);

        Assert.Equal("json_object", result.ResponseFormat!.Type);
        Assert.Equal(0.1, result.Temperature);
        Assert.Equal(baseCfg.MaxTokens, result.MaxTokens);
    }

    [Fact]
    public void ShouldApplyCallOverride_WhenTaskOverrideIsNull()
    {
        var baseCfg = MakeBase();
        var call = new LlmConfigOverride { ResponseFormat = LlmResponseFormat.JsonObject(), MaxTokens = 256 };

        var result = LlmConfigResolver.Resolve(baseCfg, null, call);

        Assert.Equal("json_object", result.ResponseFormat!.Type);
        Assert.Equal(256, result.MaxTokens);
        Assert.Equal(baseCfg.Temperature, result.Temperature);
    }

    [Fact]
    public void ShouldGiveCallOverridePrecedence_WhenBothPresent()
    {
        var baseCfg = MakeBase();
        var task = new LlmConfigOverride { ResponseFormat = LlmResponseFormat.Text(), Temperature = 0.1 };
        var call = new LlmConfigOverride { ResponseFormat = LlmResponseFormat.JsonObject(), Temperature = 0.9 };

        var result = LlmConfigResolver.Resolve(baseCfg, task, call);

        Assert.Equal("json_object", result.ResponseFormat!.Type);
        Assert.Equal(0.9, result.Temperature);
    }

    [Fact]
    public void ShouldKeepBaseValue_WhenOverrideFieldIsNullButOtherSet()
    {
        var baseCfg = MakeBase() with { Temperature = 0.7 };
        var task = new LlmConfigOverride { ResponseFormat = LlmResponseFormat.JsonObject() }; // Temperature null

        var result = LlmConfigResolver.Resolve(baseCfg, task, null);

        Assert.Equal(0.7, result.Temperature); // base preserved
        Assert.Equal("json_object", result.ResponseFormat!.Type);
    }

    [Fact]
    public void ShouldCombineFieldsAcrossOverrides_WhenDisjoint()
    {
        var baseCfg = MakeBase();
        var task = new LlmConfigOverride { Temperature = 0.2 };
        var call = new LlmConfigOverride { MaxTokens = 1024 };

        var result = LlmConfigResolver.Resolve(baseCfg, task, call);

        Assert.Equal(0.2, result.Temperature);
        Assert.Equal(1024, result.MaxTokens);
        Assert.Equal(baseCfg.TopP, result.TopP);
        Assert.Null(result.ResponseFormat);
    }

    [Fact]
    public void ShouldPropagateResponseFormat_FromCallOverrideOnly()
    {
        var baseCfg = MakeBase();
        var call = LlmConfigOverride.ForResponseFormat(LlmResponseFormat.JsonObject());

        var result = LlmConfigResolver.Resolve(baseCfg, null, call);

        Assert.NotNull(result.ResponseFormat);
        Assert.Equal("json_object", result.ResponseFormat!.Type);
    }

    [Fact]
    public void ShouldThrow_WhenBaseConfigIsNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => LlmConfigResolver.Resolve(null!, null, null));
    }
}
