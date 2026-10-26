using Orkeon.Cli.Abstractions.Runners;

namespace Orkeon.Cli.Abstractions.Tests.Runners;

/// <summary>
/// The call's input of a pending approval as the status line and the scripts' <c>approvals</c> service show it.
/// </summary>
public sealed class PendingToolApprovalInputTests
{
    [Fact]
    public void ShouldKeepTheInputWhole_WhenItFitsTheLimit()
    {
        var arguments = new string('a', PendingToolApprovalInput.MaxLength);

        Assert.Equal(arguments, PendingToolApprovalInput.Summarize(new PendingToolApproval("read", arguments, "t1", "dev-batches")));
    }

    [Fact]
    public void ShouldCutTheInputWithAnEllipsis_WhenItExceedsTheLimit()
    {
        var arguments = new string('a', PendingToolApprovalInput.MaxLength) + "tail";

        var summary = PendingToolApprovalInput.Summarize(arguments);

        Assert.Equal(new string('a', PendingToolApprovalInput.MaxLength) + "…", summary);
    }

    [Fact]
    public void ShouldReadNullArgumentsAsEmpty()
    {
        Assert.Equal(string.Empty, PendingToolApprovalInput.Summarize((string?)null));
    }
}
