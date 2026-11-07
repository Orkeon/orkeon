using System.Diagnostics;
using Orkeon.Cli.TerminalGui.Telemetry;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Telemetry;

// Shares the collection of ToolActivityAggregatorTests: both emit on the process-wide "Orkeon.Scripting" source.
[Collection("ToolActivityAggregator")]
public sealed class ToolActivityAggregatorReportedTests
{
    private static void RunTool(string tool, bool reported = false)
    {
        using var source = new ActivitySource("Orkeon.Scripting");
        using var activity = source.StartActivity("tool");
        activity?.SetTag("gen_ai.operation.name", "execute_tool");
        activity?.SetTag("gen_ai.tool.name", tool);
        if (reported) activity?.SetTag("orkeon.tool.reported", true);
    }

    [Fact]
    public void A_call_no_step_reports_is_counted()
    {
        // Arrange
        using var aggregator = new ToolActivityAggregator();
        RunTool("direct_read");

        // Act
        var sentence = aggregator.DrainSentence();

        // Assert
        Assert.Equal("Used direct_read ×1", sentence);
    }

    [Fact]
    public void A_call_a_step_reports_is_left_to_the_step()
    {
        // Arrange
        using var aggregator = new ToolActivityAggregator();
        RunTool("direct_read");
        RunTool("direct_read", reported: true);

        // Act
        var sentence = aggregator.DrainSentence();

        // Assert
        Assert.Equal("Used direct_read ×1", sentence);
    }
}
