namespace Orkeon.Hosting.Tests;

/// <summary>
/// Tests for <see cref="RunnerEnvironment"/> and the
/// <see cref="RunnerOptionsBase.EffectiveAllowExternalMounts"/> contract: the
/// <c>ORKEON_ALLOW_EXTERNAL_MOUNTS</c> env var acts as a deployment-level default
/// for the <c>--allow-external-mounts</c> flag (used by the orkeon-runners image,
/// where the container boundary already sandboxes every reachable path).
/// Env-var manipulation is process-global, so these tests must not run in
/// parallel with anything else reading the same variable.
/// </summary>
[Collection("RunnerEnvironment")]
public class RunnerEnvironmentTests
{
    private sealed class TestOptions : RunnerOptionsBase { }

    private static void WithEnv(string? value, Action assert)
    {
        var original = Environment.GetEnvironmentVariable(RunnerEnvironment.AllowExternalMountsVariable);
        try
        {
            Environment.SetEnvironmentVariable(RunnerEnvironment.AllowExternalMountsVariable, value);
            assert();
        }
        finally
        {
            Environment.SetEnvironmentVariable(RunnerEnvironment.AllowExternalMountsVariable, original);
        }
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData("Yes")]
    public void AllowExternalMounts_true_for_truthy_values(string value)
    {
        WithEnv(value, () => Assert.True(RunnerEnvironment.AllowExternalMounts));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("on")]
    public void AllowExternalMounts_false_for_unset_or_non_truthy_values(string? value)
    {
        WithEnv(value, () => Assert.False(RunnerEnvironment.AllowExternalMounts));
    }

    [Fact]
    public void EffectiveAllowExternalMounts_false_by_default()
    {
        WithEnv(null, () => Assert.False(new TestOptions().EffectiveAllowExternalMounts));
    }

    [Fact]
    public void EffectiveAllowExternalMounts_true_via_flag()
    {
        WithEnv(null, () =>
            Assert.True(new TestOptions { AllowExternalMounts = true }.EffectiveAllowExternalMounts));
    }

    [Fact]
    public void EffectiveAllowExternalMounts_true_via_env_var_without_flag()
    {
        WithEnv("1", () => Assert.True(new TestOptions().EffectiveAllowExternalMounts));
    }
}
