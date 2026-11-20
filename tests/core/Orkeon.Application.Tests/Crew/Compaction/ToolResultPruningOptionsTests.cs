using Orkeon.Application.Crew.Compaction;

namespace Orkeon.Application.Tests.Crew.Compaction;

/// <summary>#434: what <c>Orkeon:Compaction:Prune</c> refuses at start, and what it accepts.</summary>
public sealed class ToolResultPruningOptionsTests
{
    public static TheoryData<string, double, int, int, string?> Cases => new()
    {
        { "the defaults", ToolResultPruningOptions.DefaultFillShare, ToolResultPruningOptions.DefaultKeepRecentMessages, ToolResultPruningOptions.DefaultHeadChars, null },
        { "the whole window, no message kept, one character", 1.0, 0, 1, null },
        { "a share of zero", 0.0, 6, 300, "Orkeon:Compaction:Prune:FillShare is '0'" },
        { "a share above one", 1.5, 6, 300, "Orkeon:Compaction:Prune:FillShare is '1.5'" },
        { "a negative message count", 0.4, -1, 300, "Orkeon:Compaction:Prune:KeepRecentMessages is '-1'" },
        { "a head of nothing", 0.4, 6, 0, "Orkeon:Compaction:Prune:HeadChars is '0'" },
    };

    [Theory]
    [Trait("BR", "CH-434")]
    [MemberData(nameof(Cases))]
    public void ShouldNameTheProblem_WhenASettingIsOutOfRange(string name, double share, int keep, int head, string? expected)
    {
        // Arrange
        _ = name;
        var options = new ToolResultPruningOptions { FillShare = share, KeepRecentMessages = keep, HeadChars = head };

        // Act
        var problems = ToolResultPruningOptions.Problems(options);

        // Assert
        if (expected is null)
            Assert.Empty(problems);
        else
            Assert.StartsWith(expected, Assert.Single(problems), StringComparison.Ordinal);
    }
}
