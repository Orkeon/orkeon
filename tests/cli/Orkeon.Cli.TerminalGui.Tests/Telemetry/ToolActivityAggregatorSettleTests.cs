using System.Diagnostics;
using Orkeon.Cli.TerminalGui.Telemetry;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Telemetry;

public sealed class ToolActivityAggregatorSettleTests
{
    private static void RunTool(string tool)
    {
        using var source = new ActivitySource("Orkeon.Scripting");
        using var activity = source.StartActivity("tool");
        activity?.SetTag("gen_ai.operation.name", "execute_tool");
        activity?.SetTag("gen_ai.tool.name", tool);
    }

    [Fact]
    public void Calls_a_step_reported_are_not_counted_again()
    {
        // Arrange
        using var aggregator = new ToolActivityAggregator();
        RunTool("settled_read");
        RunTool("settled_read");
        aggregator.Settle(new Dictionary<string, int> { ["settled_read"] = 1 });

        // Act
        var sentence = aggregator.DrainSentence();

        // Assert
        Assert.Equal("Used settled_read ×1", sentence);
    }

    [Fact]
    public void Settling_more_than_ran_leaves_no_sentence()
    {
        // Arrange
        using var aggregator = new ToolActivityAggregator();
        aggregator.Settle(new Dictionary<string, int> { ["never_ran"] = 2 });

        // Act
        var sentence = aggregator.DrainSentence();

        // Assert
        Assert.Equal("", sentence);
    }
}
