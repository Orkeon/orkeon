using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Orkeon.Application.EventHub;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Tools.EventHub.Tests;

public sealed class WaitForEventToolTests : IClassFixture<EventHubToolsFixture>
{
    private readonly EventHubToolsFixture _fx;
    public WaitForEventToolTests(EventHubToolsFixture fx) => _fx = fx;

    [Fact]
    public async System.Threading.Tasks.Task Empty_request_fails_validation_with_exclusivity_message()
    {
        var parameters = new Dictionary<string, object?> { ["topic"] = "x" };
        var response = await _fx.WaitForEvent.CallAsync(new ToolCallRequest("wait_for_event", parameters), TestContext.Current.CancellationToken);
        Assert.False(response.Success);
        Assert.Contains("Exactly one of timeout_ms / wait_forever", response.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task Both_timeout_and_wait_forever_fails_validation()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["topic"] = "x",
            ["timeout_ms"] = 100,
            ["wait_forever"] = true
        };
        var response = await _fx.WaitForEvent.CallAsync(new ToolCallRequest("wait_for_event", parameters), TestContext.Current.CancellationToken);
        Assert.False(response.Success);
    }

    [Fact]
    public async System.Threading.Tasks.Task Finite_timeout_expired_returns_timed_out_true()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["topic"] = "never.published.tools",
            ["timeout_ms"] = 80
        };

        var response = await _fx.WaitForEvent.CallAsync(new ToolCallRequest("wait_for_event", parameters), TestContext.Current.CancellationToken);

        Assert.True(response.Success);
        var result = response.ResultDict();
        Assert.True(result.ContainsKey("timed_out"));
        Assert.True((bool)result["timed_out"]!);
    }

    [Fact]
    public async System.Threading.Tasks.Task Forever_with_cancelled_token_returns_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(80);
        var parameters = new Dictionary<string, object?>
        {
            ["topic"] = "never.published.tools.forever",
            ["wait_forever"] = true
        };

        var response = await _fx.WaitForEvent.CallAsync(
            new ToolCallRequest("wait_for_event", parameters),
            cts.Token);

        // Cancellation surfaces as Success=false / cancelled=true (ToolBase contract).
        Assert.False(response.Success);
        Assert.True(response.Metadata?["cancelled"] as bool? ?? false);
    }

    [Fact]
    public async System.Threading.Tasks.Task Received_message_returns_typed_envelope_in_result()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["topic"] = "orders.tools.received",
            ["timeout_ms"] = 2000
        };

        // Start the wait — registers eagerly. Then publish.
        var waitTask = _fx.WaitForEvent.CallAsync(new ToolCallRequest("wait_for_event", parameters), TestContext.Current.CancellationToken);
        await System.Threading.Tasks.Task.Yield();
        await _fx.Hub.PublishAsync(
            "orders.tools.received",
            JsonNode.Parse("""{ "id": "o-42" }""")!,
            options: null,
            CancellationToken.None);

        var response = await waitTask;
        Assert.True(response.Success);
        var result = response.ResultDict();
        Assert.False((bool)result["timed_out"]!);
    }

    [Fact]
    [Trait("BR", "CH-05")]
    public async System.Threading.Tasks.Task Wait_for_event_matches_a_key_value_array_metadata_match()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["topic"] = "orders.tools.kvmatch",
            ["timeout_ms"] = 2000,
            ["metadata_match"] = JsonDocument.Parse("""[ { "key": "lang", "value": "fr" } ]""").RootElement
        };

        var waitTask = _fx.WaitForEvent.CallAsync(new ToolCallRequest("wait_for_event", parameters), TestContext.Current.CancellationToken);
        await System.Threading.Tasks.Task.Yield();
        await _fx.Hub.PublishAsync(
            "orders.tools.kvmatch",
            JsonNode.Parse("""{ "id": "o-43" }""")!,
            new PublishOptions { Metadata = ImmutableDictionary<string, string>.Empty.Add("lang", "fr") },
            CancellationToken.None);

        var response = await waitTask;
        Assert.True(response.Success, response.Error);
        Assert.False((bool)response.ResultDict()["timed_out"]!);
    }
}
