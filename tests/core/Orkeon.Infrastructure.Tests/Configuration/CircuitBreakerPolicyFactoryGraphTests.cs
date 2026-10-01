using Orkeon.Domain.Common.StateMachine;
using Orkeon.Domain.Configuration;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// Coverage for <see cref="CircuitBreakerPolicyFactory.ResolveGraph"/> (P2-O-01): mapping a
/// graph-specific <see cref="GraphConfig"/> (preset + node/visit/duration overrides) to a
/// <see cref="CircuitBreakerPolicy"/>, falling back to the strategy default without one. Since
/// GAP-07 <c>graphConfig</c> is the only circuit-breaker setting a crew carries.
/// </summary>
public sealed class CircuitBreakerPolicyFactoryGraphTests
{
    [Fact]
    public void ResolveGraph_ShouldReturnFallbackUnchanged_WhenNoConfig()
    {
        var fallback = CircuitBreakerPolicy.Strict;

        var resolved = CircuitBreakerPolicyFactory.ResolveGraph(null, fallback);

        // Reference-equal: the default path must be byte-identical to the strategy fallback.
        Assert.Same(fallback, resolved);
    }

    [Fact]
    public void ResolveGraph_ShouldMapPreset_WhenGraphConfigHasOnlyPreset()
    {
        var resolved = CircuitBreakerPolicyFactory.ResolveGraph(
            new GraphConfig { CircuitBreakerPreset = "permissive" }, CircuitBreakerPolicy.Strict);

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

        var resolved = CircuitBreakerPolicyFactory.ResolveGraph(graphConfig, CircuitBreakerPolicy.Strict);

        Assert.Equal(8, resolved.MaxTransitions);
        Assert.Equal(3, resolved.MaxStateVisits);
        Assert.Equal(TimeSpan.FromSeconds(120), resolved.MaxTotalDuration);
        // GraphConfig has no state-timeout / degraded-mode fields — those stay at the preset value.
        Assert.Equal(CircuitBreakerPolicy.Permissive.StateTimeout, resolved.StateTimeout);
        Assert.Equal(CircuitBreakerPolicy.Permissive.UseDegradedMode, resolved.UseDegradedMode);
    }

    [Theory]
    [InlineData("strict", 50)]
    [InlineData("Permissive", 1000)]
    [InlineData("default", 100)]
    [InlineData("bogus", 50)]
    [InlineData(null, 50)]
    public void ResolveGraph_ShouldMapEveryPreset_AndFallBackToStrict(string? preset, int expectedMaxTransitions)
    {
        var resolved = CircuitBreakerPolicyFactory.ResolveGraph(
            new GraphConfig { CircuitBreakerPreset = preset! }, CircuitBreakerPolicy.Permissive);

        Assert.Equal(expectedMaxTransitions, resolved.MaxTransitions);
    }
}
