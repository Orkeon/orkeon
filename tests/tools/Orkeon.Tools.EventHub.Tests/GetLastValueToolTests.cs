using System.Text.Json.Nodes;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Tools.EventHub.Tests;

public sealed class GetLastValueToolTests : IClassFixture<EventHubToolsFixture>
{
    private readonly EventHubToolsFixture _fx;
    public GetLastValueToolTests(EventHubToolsFixture fx) => _fx = fx;

    [Fact]
    public async System.Threading.Tasks.Task Returns_found_false_when_key_absent()
    {
        var parameters = new Dictionary<string, object?> { ["key"] = "absent" };
        var response = await _fx.GetLastValue.CallAsync(new ToolCallRequest("get_last_value", parameters), TestContext.Current.CancellationToken);
        Assert.True(response.Success);
        Assert.False((bool)response.ResultDict()["found"]!);
    }

    [Fact]
    public async System.Threading.Tasks.Task Returns_retained_value_after_PublishAsync_with_retain_flag()
    {
        var crewId = CrewId.Create();
        await _fx.Hub.PublishAsync(
            "config.update",
            JsonNode.Parse("""{ "mode": "rw" }""")!,
            new PublishOptions
            {
                TargetCrewId = crewId,
                RetainAsLastValue = true,
                LastValueKey = "config"
            },
            CancellationToken.None);

        var parameters = new Dictionary<string, object?>
        {
            ["key"] = "config",
            ["crew_scope"] = crewId.ToString()
        };
        var response = await _fx.GetLastValue.CallAsync(new ToolCallRequest("get_last_value", parameters), TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        Assert.True((bool)response.ResultDict()["found"]!);
    }
}
