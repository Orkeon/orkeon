using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Compaction;
using Orkeon.Infrastructure.LLMs.Context;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Context;

/// <summary>
/// #432: the window a loop compacts against, from the profile, then the documented table, then
/// <c>Orkeon:Cli:Session:ContextWindowTokens</c> with one warning per profile; the source says which.
/// </summary>
public sealed class ContextWindowResolverTests
{
    [Theory]
    [Trait("BR", "CH-432")]
    [InlineData(64_000, "kimi-k3", 64_000, ContextWindowSources.Profile)]
    [InlineData(null, "kimi-k3", 1_000_000, ContextWindowSources.Table)]
    [InlineData(null, "unknown-model", 150_000, ContextWindowSources.Settings)]
    [InlineData(null, null, 150_000, ContextWindowSources.Settings)]
    public void ShouldResolveFromTheProfileThenTheTableThenTheSettings_WhenEachIsMissingInTurn(
        int? profileWindow, string? model, int expectedTokens, string expectedSource)
    {
        // Arrange
        var resolver = new ContextWindowResolver(150_000, new RecordingLogger());

        // Act
        var window = resolver.Resolve("default", model, profileWindow);

        // Assert
        Assert.Equal(new ContextWindow(expectedTokens, expectedSource), window);
    }

    [Fact]
    [Trait("BR", "CH-432")]
    public void ShouldFallBackOnTheSessionDefault_WhenTheSettingsSetNoWindow()
    {
        // Arrange
        var resolver = new ContextWindowResolver(null, new RecordingLogger());

        // Act
        var window = resolver.Resolve("default", "unknown-model", null);

        // Assert
        Assert.Equal(new ContextWindow(200_000, ContextWindowSources.Settings), window);
    }

    [Fact]
    [Trait("BR", "CH-432")]
    public void ShouldWarnOncePerProfileNamingTheFigure_WhenTheWindowIsAssumed()
    {
        // Arrange
        var logger = new RecordingLogger();
        var resolver = new ContextWindowResolver(150_000, logger);

        // Act
        resolver.Resolve("fast", "unknown-model", null);
        resolver.Resolve("fast", "unknown-model", null);
        resolver.Resolve("deep", "unknown-model", null);

        // Assert
        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.Contains("'fast'", warnings[0]);
        Assert.Contains("150000", warnings[0]);
        Assert.Contains("'deep'", warnings[1]);
    }
}
