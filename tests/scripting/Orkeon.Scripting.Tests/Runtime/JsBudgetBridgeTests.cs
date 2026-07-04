using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// F1: bridge from the untyped <c>crewBuilder().budget({...})</c> dictionary to the typed
/// <see cref="Orkeon.Domain.Autonomous.AgentExecutionBudget"/>. Null-spec and empty-spec
/// must yield null (legacy crews keep the unbudgeted behaviour).
/// </summary>
public sealed class JsBudgetBridgeTests
{
    [Fact]
    public void FromSpec_null_or_empty_returns_null()
    {
        Assert.Null(JsBudgetBridge.FromSpec(null));
        Assert.Null(JsBudgetBridge.FromSpec(new Dictionary<string, object?>()));
    }

    [Fact]
    public void FromSpec_unrecognized_keys_only_returns_null()
    {
        var spec = new Dictionary<string, object?> { ["somethingElse"] = 42d };
        Assert.Null(JsBudgetBridge.FromSpec(spec));
    }

    [Fact]
    public void FromSpec_maps_jint_doubles_to_integer_dimensions()
    {
        // Jint's ToObject() surfaces JS numbers as double — the exp07 crew shape.
        var spec = new Dictionary<string, object?>
        {
            ["toolCalls"] = 200d,
            ["tokens"] = 400_000d,
            ["delegationDepth"] = 2d,
            ["spawnedAgents"] = 4d,
        };

        var budget = JsBudgetBridge.FromSpec(spec);

        Assert.NotNull(budget);
        Assert.Equal(200, budget.MaxToolCalls);
        Assert.Equal(400_000, budget.MaxTokensConsumed);
        Assert.Equal(2, budget.MaxDelegationDepth);
        Assert.Equal(4, budget.MaxSpawnedAgents);
    }

    [Fact]
    public void FromSpec_wallTime_number_means_seconds()
    {
        var budget = JsBudgetBridge.FromSpec(new Dictionary<string, object?> { ["wallTime"] = 600d });

        Assert.NotNull(budget);
        Assert.Equal(TimeSpan.FromSeconds(600), budget.MaxWallTime);
    }

    [Theory]
    [InlineData("500ms", 0.5)]
    [InlineData("45s", 45)]
    [InlineData("10m", 600)]
    [InlineData("1h", 3600)]
    [InlineData("90", 90)] // bare string number = seconds, like the numeric form
    public void FromSpec_wallTime_string_parses_units(string text, double expectedSeconds)
    {
        var budget = JsBudgetBridge.FromSpec(new Dictionary<string, object?> { ["wallTime"] = text });

        Assert.NotNull(budget);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), budget.MaxWallTime);
    }

    [Fact]
    public void FromSpec_invalid_values_fall_back_to_defaults_without_throwing()
    {
        var spec = new Dictionary<string, object?>
        {
            ["toolCalls"] = "not-a-number",
            ["tokens"] = -5d,
            ["wallTime"] = "yesterday",
        };

        // Every value is invalid → no recognized dimension parsed → null (no enforcement),
        // and crucially no exception at crew build time.
        Assert.Null(JsBudgetBridge.FromSpec(spec));

        // Mixed valid/invalid: valid keys apply, invalid ones keep their defaults.
        var mixed = JsBudgetBridge.FromSpec(new Dictionary<string, object?>
        {
            ["toolCalls"] = 3d,
            ["wallTime"] = "yesterday",
        });
        Assert.NotNull(mixed);
        Assert.Equal(3, mixed.MaxToolCalls);
        Assert.Equal(Orkeon.Domain.Autonomous.AgentExecutionBudget.Default.MaxWallTime, mixed.MaxWallTime);
    }
}
