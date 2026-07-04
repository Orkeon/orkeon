using Orkeon.Domain.HumanInput;
using Orkeon.Infrastructure.HumanInput;

namespace Orkeon.Infrastructure.Tests.HumanInput;

public class AutoApproveHumanInputProviderTests
{
    [Fact]
    public async Task GetInputAsync_ReturnsDefault_WhenContextHasDefault()
    {
        var provider = new AutoApproveHumanInputProvider();
        var ctx = new HumanInputContext { Prompt = "Q?", DefaultValue = "yes_please" };

        var result = await provider.GetInputAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal("yes_please", result);
    }

    [Fact]
    public async Task GetInputAsync_ReturnsApproved_WhenNoDefault()
    {
        var provider = new AutoApproveHumanInputProvider();
        var ctx = new HumanInputContext { Prompt = "Q?" };

        var result = await provider.GetInputAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal("approved", result);
    }

    [Fact]
    public async Task GetConfirmationAsync_AlwaysTrue()
    {
        var provider = new AutoApproveHumanInputProvider();
        var ctx = new HumanInputContext { Prompt = "Confirm?" };

        Assert.True(await provider.GetConfirmationAsync(ctx, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetChoiceAsync_PrefersDefault_OverFirstOption()
    {
        var provider = new AutoApproveHumanInputProvider();
        var ctx = new HumanInputContext
        {
            Prompt = "Pick",
            DefaultValue = "ok",
            Options = new[] { "first", "second" },
        };

        Assert.Equal("ok", await provider.GetChoiceAsync(ctx, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetChoiceAsync_FallsBackToFirstOption_WhenNoDefault()
    {
        var provider = new AutoApproveHumanInputProvider();
        var ctx = new HumanInputContext
        {
            Prompt = "Pick",
            Options = new[] { "alpha", "beta" },
        };

        Assert.Equal("alpha", await provider.GetChoiceAsync(ctx, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetChoiceAsync_FallsBackToApproved_WhenNoDefaultAndNoOptions()
    {
        var provider = new AutoApproveHumanInputProvider();
        var ctx = new HumanInputContext { Prompt = "Pick" };

        Assert.Equal("approved", await provider.GetChoiceAsync(ctx, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsAvailableAsync_AlwaysTrue()
    {
        var provider = new AutoApproveHumanInputProvider();

        Assert.True(await provider.IsAvailableAsync(TestContext.Current.CancellationToken));
    }
}
