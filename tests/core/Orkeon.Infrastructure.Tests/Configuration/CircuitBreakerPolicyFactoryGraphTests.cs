using Orkeon.Domain.Common.StateMachine;
using Orkeon.Domain.Configuration;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// Coverage for <see cref="CircuitBreakerPolicyFactory.ResolveGraph"/> (P2-O-01): mapping a
/// graph-specific <see cref="GraphConfig"/> (preset + node/visit/duration overrides) to a
/// <see cref="CircuitBreakerPolicy"/>, with fallback precedence GraphConfig → crew config → default.
/// </summary>
public sealed class CircuitBreakerPolicyFactoryGraphTests
{
    [Fact]
    public void ResolveGraph_ShouldReturnFallbackUnchanged_WhenNoConfig()
    {
        var fallback = CircuitBreakerPolicy.Strict;

        var resolved = CircuitBreakerPolicyFactory.ResolveGraph(null, null, fallback);

        // Reference-equal: the default path must be byte-identical to the strategy fallback.
        Assert.Same(fallback, resolved);
    }

    [Fact]
    public void ResolveGraph_ShouldMapPreset_WhenGraphConfigHasOnlyPreset()
    {
        var resolved = CircuitBreakerPolicyFactory.ResolveGraph(
            new GraphConfig { CircuitBreakerPreset = "permissive" }, null, CircuitBreakerPolicy.Strict);

        // Permissive preset values flow through untouched.
        Assert.Equal(CircuitBreakerPolicy.Permissive.MaxTransitions, resolved.MaxTransitions);
        Assert.Equal(CircuitBreakerPolicy.Permissive.MaxStateVisits, resolved.MaxStateVisits);
        Assert.Equal(CircuitBreakerPolicy.Permissive.MaxTotalDuration, resolved.MaxTotalDuration);
    }

    [Fact]
    public void ResolveGraph_ShouldApplyFieldOverrides_OverPreset()
    {
        var graphConfig = new GraphConfig
        {
            CircuitBreakerPreset = "permissive",
            MaxTransitions = 8,
            MaxStateVisits = 3,
            MaxTotalDurationSeconds = 120
        };

        var resolved = CircuitBreakerPolicyFactory.ResolveGraph(graphConfig, null, CircuitBreakerPolicy.Strict);

        Assert.Equal(8, resolved.MaxTransitions);
        Assert.Equal(3, resolved.MaxStateVisits);
        Assert.Equal(TimeSpan.FromSeconds(120), resolved.MaxTotalDuration);
        // GraphConfig has no state-timeout / degraded-mode fields — those stay at the preset value.
        Assert.Equal(CircuitBreakerPolicy.Permissive.StateTimeout, resolved.StateTimeout);
        Assert.Equal(CircuitBreakerPolicy.Permissive.UseDegradedMode, resolved.UseDegradedMode);
    }

    [Fact]
    public void ResolveGraph_ShouldFallBackToCrewCircuitBreaker_WhenNoGraphConfig()
    {
        var crewDefault = new CircuitBreakerConfig { Preset = "permissive", MaxTransitions = 42 };

        var resolved = CircuitBreakerPolicyFactory.ResolveGraph(null, crewDefault, CircuitBreakerPolicy.Strict);

        Assert.Equal(42, resolved.MaxTransitions);
        Assert.Equal(CircuitBreakerPolicy.Permissive.MaxStateVisits, resolved.MaxStateVisits);
    }

    [Fact]
    public void ResolveGraph_ShouldPreferGraphConfig_OverCrewCircuitBreaker()
    {
        var graphConfig = new GraphConfig { CircuitBreakerPreset = "permissive", MaxTransitions = 8 };
        var crewDefault = new CircuitBreakerConfig { Preset = "strict", MaxTransitions = 999 };

        var resolved = CircuitBreakerPolicyFactory.ResolveGraph(graphConfig, crewDefault, CircuitBreakerPolicy.Strict);

        Assert.Equal(8, resolved.MaxTransitions);
    }
}
