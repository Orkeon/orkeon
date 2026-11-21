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
        // Jint's ToObject() surfaces JS numbers as double — the real crew shape.
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

    // A procedural crew's budget is read when its run starts: an unreadable limit is refused, as the run option and the
    // declarative crew refuse it, never read as "uncapped" (#436 made a left-out dimension uncapped).
    [Theory]
    [Trait("BR", "CH-436")]
    [InlineData("toolCalls", "not-a-number", "budget.toolCalls must be a whole number of 1 or more; got \"not-a-number\".")]
    [InlineData("tokens", -5d, "budget.tokens must be a whole number of 1 or more; got -5.")]
    [InlineData("contextTokens", 2.5d, "budget.contextTokens must be a whole number of 1 or more; got 2.5.")]
    [InlineData("wallTime", "5 min", "budget.wallTime must be a positive number of seconds or a duration such as \"90s\" or \"5m\"; got \"5 min\".")]
    [InlineData("wallTime", 0d, "budget.wallTime must be a positive number of seconds or a duration such as \"90s\" or \"5m\"; got 0.")]
    public void FromSpec_refuses_a_limit_it_cannot_read(string key, object value, string expected)
    {
        // Arrange
        var spec = new Dictionary<string, object?> { ["toolCalls"] = 20d, [key] = value };

        // Act
        var ex = Assert.Throws<Orkeon.Scripting.Exceptions.InvalidScriptException>(() => JsBudgetBridge.FromSpec(spec));

        // Assert
        Assert.Equal("crewBuilder().budget({...}): " + expected, ex.Message);
    }

    // #436: a dimension left out takes the value it has when no budget is named.

    [Theory]
    [Trait("BR", "CH-436")]
    [InlineData("spawnedAgents")]
    [InlineData("delegationDepth")]
    public void FromSpec_naming_one_structural_limit_sets_no_wall_clock_tool_call_or_token_cap(string key)
    {
        // Arrange
        var spec = new Dictionary<string, object?> { [key] = 3d };

        // Act
        var budget = JsBudgetBridge.FromSpec(spec);

        // Assert
        Assert.NotNull(budget);
        Assert.Equal(TimeSpan.MaxValue, budget.MaxWallTime);
        Assert.Equal(int.MaxValue, budget.MaxToolCalls);
        Assert.Equal(int.MaxValue, budget.MaxTokensConsumed);
        Assert.Equal(int.MaxValue, budget.MaxContextTokens);
    }

    [Fact]
    [Trait("BR", "CH-436")]
    public void FromSpec_keeps_the_depth_and_spawn_defaults_for_the_dimensions_it_leaves_out()
    {
        // Arrange
        var spec = new Dictionary<string, object?> { ["toolCalls"] = 40d };
        var defaults = Orkeon.Domain.Autonomous.AgentExecutionBudget.Default;

        // Act
        var budget = JsBudgetBridge.FromSpec(spec);

        // Assert
        Assert.NotNull(budget);
        Assert.Equal(defaults.MaxDelegationDepth, budget.MaxDelegationDepth);
        Assert.Equal(defaults.MaxSpawnedAgents, budget.MaxSpawnedAgents);
    }

    [Fact]
    [Trait("BR", "CH-436")]
    public void FromSpec_reads_the_given_clock_when_it_charges_no_budget()
    {
        // Arrange
        var clock = new Orkeon.Tests.Shared.Timing.ManualTimeProvider();

        // Act
        var budget = JsBudgetBridge.FromSpec(new Dictionary<string, object?> { ["spawnedAgents"] = 10d }, clock: clock);

        // Assert
        Assert.NotNull(budget);
        Assert.Same(clock, budget.TimeProvider);
    }

    [Fact]
    [Trait("BR", "CH-436")]
    public void FromRunOption_naming_tool_calls_alone_sets_no_wall_clock_or_token_cap()
    {
        // Arrange
        using var engine = new Orkeon.Scripting.JsEngineFactory().Create();

        // Act
        var budget = JsBudgetBridge.FromRunOption(Js(engine, "{ toolCalls: 3 }"), chargedTo: null);

        // Assert
        Assert.NotNull(budget);
        Assert.Equal(3, budget.MaxToolCalls);
        Assert.Equal(TimeSpan.MaxValue, budget.MaxWallTime);
        Assert.Equal(int.MaxValue, budget.MaxTokensConsumed);
    }

    // #189: the budget option of crew.runAgent / ctx.delegate.

    private static Jint.Native.JsValue Js(Jint.Engine engine, string expression) => engine.Evaluate("(" + expression + ")");

    [Theory]
    [InlineData("{ wallTime: 30 }", 30d)]
    [InlineData("{ wallTime: \"2m\" }", 120d)]
    [InlineData("{ wallTime: \"500ms\" }", 0.5d)]
    public void FromRunOption_maps_wallTime_to_the_wall_time_limit(string spec, double seconds)
    {
        using var engine = new Orkeon.Scripting.JsEngineFactory().Create();

        var budget = JsBudgetBridge.FromRunOption(Js(engine, spec), chargedTo: null);

        Assert.NotNull(budget);
        Assert.Equal(TimeSpan.FromSeconds(seconds), budget.MaxWallTime);
        Assert.Null(budget.ChargedTo);
    }

    [Fact]
    public void FromRunOption_charges_the_given_budget_and_reads_its_clock()
    {
        using var engine = new Orkeon.Scripting.JsEngineFactory().Create();
        var clock = new Orkeon.Tests.Shared.Timing.ManualTimeProvider();
        var crew = new Orkeon.Domain.Autonomous.AgentExecutionBudget { TimeProvider = clock };

        var budget = JsBudgetBridge.FromRunOption(Js(engine, "{ toolCalls: 3, tokens: 500 }"), crew);

        Assert.NotNull(budget);
        Assert.Equal(3, budget.MaxToolCalls);
        Assert.Equal(500, budget.MaxTokensConsumed);
        Assert.Same(crew, budget.ChargedTo);
        Assert.Same(clock, budget.TimeProvider);
    }

    [Fact]
    [Trait("BR", "CH-436")]
    public void FromRunOption_reads_the_given_clock_when_it_charges_no_budget()
    {
        // Arrange
        using var engine = new Orkeon.Scripting.JsEngineFactory().Create();
        var clock = new Orkeon.Tests.Shared.Timing.ManualTimeProvider();

        // Act
        var budget = JsBudgetBridge.FromRunOption(Js(engine, "{ wallTime: 60 }"), chargedTo: null, clock);

        // Assert
        Assert.NotNull(budget);
        Assert.Same(clock, budget.TimeProvider);
    }

    [Fact]
    public void FromRunOption_without_a_dimension_returns_null()
    {
        using var engine = new Orkeon.Scripting.JsEngineFactory().Create();

        Assert.Null(JsBudgetBridge.FromRunOption(Js(engine, "{}"), chargedTo: null));
    }

    [Fact]
    public void FromRunOption_refuses_what_FromSpec_would_skip()
    {
        using var engine = new Orkeon.Scripting.JsEngineFactory().Create();

        var ex = Assert.Throws<Orkeon.Scripting.Exceptions.InvalidScriptException>(
            () => JsBudgetBridge.FromRunOption(Js(engine, "{ toolCalls: -5 }"), chargedTo: null));

        Assert.Equal("crew.runAgent / ctx.delegate options: budget.toolCalls must be a whole number of 1 or more; got -5.", ex.Message);
    }

    // #224: contextTokens, the context of a turn counted once.

    [Fact]
    [Trait("BR", "CH-224")]
    public void FromRunOption_maps_contextTokens_and_leaves_the_sum_uncapped()
    {
        using var engine = new Orkeon.Scripting.JsEngineFactory().Create();

        var budget = JsBudgetBridge.FromRunOption(Js(engine, "{ contextTokens: 200000 }"), chargedTo: null);

        Assert.NotNull(budget);
        Assert.Equal(200_000, budget.MaxContextTokens);
        Assert.Equal(int.MaxValue, budget.MaxTokensConsumed);
    }

    [Fact]
    [Trait("BR", "CH-224")]
    public void FromSpec_keeps_both_measures_when_both_keys_are_set()
    {
        var spec = new Dictionary<string, object?> { ["tokens"] = 900_000d, ["contextTokens"] = 150_000d };

        var budget = JsBudgetBridge.FromSpec(spec);

        Assert.NotNull(budget);
        Assert.Equal(900_000, budget.MaxTokensConsumed);
        Assert.Equal(150_000, budget.MaxContextTokens);
    }

    [Fact]
    [Trait("BR", "CH-224")]
    public void FromSpec_without_contextTokens_keeps_the_context_uncapped()
    {
        var budget = JsBudgetBridge.FromSpec(new Dictionary<string, object?> { ["tokens"] = 500d });

        Assert.NotNull(budget);
        Assert.Equal(500, budget.MaxTokensConsumed);
        Assert.Equal(int.MaxValue, budget.MaxContextTokens);
    }

    [Fact]
    [Trait("BR", "CH-224")]
    public void FromRunOption_refuses_a_contextTokens_that_is_not_a_whole_number()
    {
        using var engine = new Orkeon.Scripting.JsEngineFactory().Create();

        var ex = Assert.Throws<Orkeon.Scripting.Exceptions.InvalidScriptException>(
            () => JsBudgetBridge.FromRunOption(Js(engine, "{ contextTokens: 0 }"), chargedTo: null));

        Assert.Equal("crew.runAgent / ctx.delegate options: budget.contextTokens must be a whole number of 1 or more; got 0.", ex.Message);
    }
}
