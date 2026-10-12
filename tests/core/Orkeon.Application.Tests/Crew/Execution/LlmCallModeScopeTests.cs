using Orkeon.Application.Crew.Execution;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>The run's fallback memory: a nested run shares the enclosing one's, the outermost ends it.</summary>
public sealed class LlmCallModeScopeTests
{
    [Fact]
    [Trait("BR", "BR-06")]
    public void ShouldKeepTheEnclosingRunsFallbacks_WhenANestedRunOpens()
    {
        using var outer = LlmCallModeScope.Open();
        LlmCallModeScope.RecordFallback("before-nested");

        using (LlmCallModeScope.Open())
        {
            Assert.True(LlmCallModeScope.HasFallenBack("before-nested"));
            LlmCallModeScope.RecordFallback("inside-nested");
        }

        Assert.True(LlmCallModeScope.HasFallenBack("before-nested"));
        Assert.True(LlmCallModeScope.HasFallenBack("inside-nested"));
    }

    [Fact]
    [Trait("BR", "BR-06")]
    public void ShouldForgetFallbacks_WhenTheOutermostRunEnds()
    {
        using (LlmCallModeScope.Open())
        {
            LlmCallModeScope.RecordFallback("a-setting");
            Assert.True(LlmCallModeScope.HasFallenBack("a-setting"));
        }

        Assert.False(LlmCallModeScope.IsOpen);
        Assert.False(LlmCallModeScope.HasFallenBack("a-setting"));

        using (LlmCallModeScope.Open())
            Assert.False(LlmCallModeScope.HasFallenBack("a-setting"));
    }
}
