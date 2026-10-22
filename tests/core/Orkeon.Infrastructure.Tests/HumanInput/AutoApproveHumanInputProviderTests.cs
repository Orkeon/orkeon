using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.HumanInput;
using Orkeon.Infrastructure.HumanInput;

namespace Orkeon.Infrastructure.Tests.HumanInput;

public class AutoApproveHumanInputProviderTests
{
    private static readonly string[] ExpectedA = ["a"];
    private static readonly string[] ExpectedB = ["b"];

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
    [Trait("BR", "RT-08")]
    public async Task ShouldReturnDefault_WhenHeadlessPolicyIsDefault()
    {
        var provider = new AutoApproveHumanInputProvider(
            new HumanInputOptions { HeadlessPolicy = HumanInputHeadlessPolicy.Default });
        var ctx = new HumanInputContext
        {
            Prompt = "Pick",
            Options = new[] { "a", "b", "c" },
            Recommended = 2,
        };

        Assert.Equal("c", await provider.GetChoiceAsync(ctx, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-08")]
    public async Task ShouldRefuse_WhenHeadlessPolicyIsRefuse()
    {
        var provider = new AutoApproveHumanInputProvider(
            new HumanInputOptions { HeadlessPolicy = HumanInputHeadlessPolicy.Refuse });
        var ctx = new HumanInputContext { Prompt = "Q?", DefaultValue = "the_default" };

        Assert.False(await provider.GetConfirmationAsync(ctx, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<HumanInputRefusedException>(
            () => provider.GetInputAsync(ctx, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<HumanInputRefusedException>(
            () => provider.GetChoiceAsync(ctx, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-08")]
    public async Task ShouldReturnRecommendedList_WhenMultiAndPolicyIsDefault()
    {
        IHumanInputProvider provider = new AutoApproveHumanInputProvider(
            new HumanInputOptions { HeadlessPolicy = HumanInputHeadlessPolicy.Default });
        var ctx = new HumanInputContext
        {
            Prompt = "Pick",
            Options = new[] { "a", "b", "c" },
            Recommended = 1,
        };
        var noRecommendation = new HumanInputContext { Prompt = "Pick", Options = new[] { "a", "b" } };

        Assert.Equal(ExpectedB, await provider.GetMultiChoiceAsync(ctx, TestContext.Current.CancellationToken));
        Assert.Equal(ExpectedA, await provider.GetMultiChoiceAsync(noRecommendation, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("BR", "RT-08")]
    public async Task ShouldRefuse_WhenMultiAndPolicyIsRefuse()
    {
        IHumanInputProvider provider = new AutoApproveHumanInputProvider(
            new HumanInputOptions { HeadlessPolicy = HumanInputHeadlessPolicy.Refuse });
        var ctx = new HumanInputContext { Prompt = "Pick", Options = new[] { "a", "b" } };

        await Assert.ThrowsAsync<HumanInputRefusedException>(
            () => provider.GetMultiChoiceAsync(ctx, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsAvailableAsync_AlwaysTrue()
    {
        IHumanInputProvider provider = new AutoApproveHumanInputProvider();

        Assert.True(await provider.IsAvailableAsync(TestContext.Current.CancellationToken));
    }
}
