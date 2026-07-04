using Orkeon.Domain.Tools.Protocol;
using Orkeon.Trading.Tools.Infrastructure.Execution;

namespace Orkeon.Examples.Runners.Tests;

public class TwapToolSmokeTests
{
    [Fact]
    public async Task Twap_returns_schedule_for_typical_request()
    {
        var tool = new TWAPExecutionTool();
        var request = new ToolCallRequest(
            ToolName: tool.Name,
            Parameters: new Dictionary<string, object?>
            {
                ["symbol"] = "AAPL",
                ["side"] = "BUY",
                ["quantity"] = 1000m,
                ["start_time"] = "09:30",
                ["end_time"] = "10:30",
                ["slice_interval_minutes"] = 10,
                ["randomize_timing"] = false,
            });

        var response = await tool.CallAsync(request, CancellationToken.None);

        Assert.True(response.Success, response.Error);
        Assert.NotNull(response.Result);
        var dict = Assert.IsType<Dictionary<string, object>>(response.Result);
        Assert.Equal("AAPL", dict["symbol"]);
        Assert.Equal("BUY", dict["side"]);
    }

    [Fact]
    public void Twap_tool_metadata_loaded_from_yaml()
    {
        var tool = new TWAPExecutionTool();
        Assert.False(string.IsNullOrWhiteSpace(tool.Name));
        Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        Assert.NotNull(tool.Schema);
    }
}
