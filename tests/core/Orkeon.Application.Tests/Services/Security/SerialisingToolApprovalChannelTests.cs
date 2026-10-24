using System.Collections.Immutable;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Application.Services.Security;
using Orkeon.Application.Tests.Doubles;

namespace Orkeon.Application.Tests.Services.Security;

/// <summary>RT-07: prompts to the operator never overlap, whatever the number of concurrent askers.</summary>
public class SerialisingToolApprovalChannelTests
{
    private static ToolApprovalRequest Request(string tool) => new()
    {
        ToolName = tool,
        Arguments = ImmutableDictionary<string, object?>.Empty,
        Mode = "default",
        Reason = "test",
    };

    [Fact]
    [Trait("BR", "RT-07")]
    public async System.Threading.Tasks.Task ShouldForwardOneRequestAtATime_WhenTwoApprovalsAskedTogether()
    {
        var inner = new RecordingApprovalChannel();
        var channel = new SerialisingToolApprovalChannel(inner);

        var first = channel.RequestAsync(Request("file_read"), TestContext.Current.CancellationToken);
        var second = channel.RequestAsync(Request("grep"), TestContext.Current.CancellationToken);
        var decisions = await System.Threading.Tasks.Task.WhenAll(first, second);

        Assert.Equal([ToolApprovalDecision.AllowOnce, ToolApprovalDecision.AllowOnce], decisions);
        Assert.Equal(1, inner.MaxConcurrent);
        Assert.Equal(["file_read", "grep"], inner.Requested);
    }
}
