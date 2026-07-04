using Orkeon.Application.EventHub;
using Orkeon.Application.EventHub.Exceptions;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Tools.EventHub.Tests;

public sealed class ReplyToToolTests : IClassFixture<EventHubToolsFixture>
{
    private readonly EventHubToolsFixture _fx;
    public ReplyToToolTests(EventHubToolsFixture fx) => _fx = fx;

    [Fact]
    public async System.Threading.Tasks.Task Unknown_correlation_id_returns_UnknownCorrelation_error()
    {
        var parameters = new Dictionary<string, object?>
        {
            ["correlation_id"] = CorrelationId.NewId().AsString(),
            ["payload"] = null
        };

        var response = await _fx.ReplyTo.CallAsync(new ToolCallRequest("reply_to", parameters), TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Equal(
            nameof(UnknownCorrelationException),
            response.Metadata?["exception_type"]?.ToString());
    }

    [Fact]
    public async System.Threading.Tasks.Task Missing_correlation_id_fails_validation()
    {
        var parameters = new Dictionary<string, object?> { ["correlation_id"] = "" };
        var response = await _fx.ReplyTo.CallAsync(new ToolCallRequest("reply_to", parameters), TestContext.Current.CancellationToken);
        Assert.False(response.Success);
        Assert.Contains("correlation_id is required", response.Error, StringComparison.Ordinal);
    }
}
