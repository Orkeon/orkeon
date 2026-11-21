using Orkeon.Application.Crew.Compaction;

namespace Orkeon.Application.Tests.Crew.Compaction;

/// <summary>#432: what <c>Orkeon:Compaction</c> refuses at start, and what it accepts.</summary>
public sealed class ContextCompactionOptionsTests
{
    public static TheoryData<string, double, int, string?> Cases => new()
    {
        { "the defaults", ContextCompactionOptions.DefaultTriggerShare, ContextCompactionOptions.DefaultKeepRecentTurns, null },
        { "the whole window", 1.0, 0, null },
        { "a share of zero", 0.0, 6, "Orkeon:Compaction:TriggerShare is '0'" },
        { "a share above one", 1.2, 6, "Orkeon:Compaction:TriggerShare is '1.2'" },
        { "a negative turn count", 0.75, -1, "Orkeon:Compaction:KeepRecentTurns is '-1'" },
    };

    [Theory]
    [Trait("BR", "CH-432")]
    [MemberData(nameof(Cases))]
    public void ShouldNameTheProblem_WhenASettingIsOutOfRange(string name, double share, int keep, string? expected)
    {
        // Arrange
        _ = name;
        var options = new ContextCompactionOptions { TriggerShare = share, KeepRecentTurns = keep };

        // Act
        var problems = ContextCompactionOptions.Problems(options);

        // Assert
        if (expected is null)
            Assert.Empty(problems);
        else
            Assert.StartsWith(expected, Assert.Single(problems), StringComparison.Ordinal);
    }
}
