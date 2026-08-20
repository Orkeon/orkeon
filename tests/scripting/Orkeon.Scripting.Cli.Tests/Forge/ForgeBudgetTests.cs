using Orkeon.Scripting.Cli.Commands.Forge;

namespace Orkeon.Scripting.Cli.Tests.Forge;

/// <summary>
/// The three-dimension session budget (SPEC-ORKEON-FORGE §4.2): hard bounds, zero means
/// unlimited for tokens and wall time, and the exhausted dimension is named — never a
/// silent stop.
/// </summary>
public class ForgeBudgetTests
{
    [Fact]
    public void Iterations_are_always_bounded_and_the_first_pass_counts()
    {
        var budget = new ForgeBudget { MaxIterations = 2 };

        Assert.True(budget.CanStartIteration);
        budget.RegisterIteration();               // the first pass
        Assert.True(budget.CanStartIteration);
        budget.RegisterIteration();               // one refine
        Assert.False(budget.CanStartIteration);   // a second refine would be the third cycle
    }

    [Fact]
    public void Zero_means_unlimited_for_tokens_and_wall_time()
    {
        var budget = new ForgeBudget();
        budget.RegisterTokens(1_000_000);
        budget.RegisterWallTime(TimeSpan.FromHours(10));

        Assert.Null(budget.ExhaustedDimension());
    }

    [Fact]
    public void The_exhausted_dimension_is_named()
    {
        var tokens = new ForgeBudget { MaxTokens = 100 };
        tokens.RegisterTokens(100);
        Assert.Equal(ForgeBudgetDimension.Tokens, tokens.ExhaustedDimension());

        var wall = new ForgeBudget { MaxWallSeconds = 60 };
        wall.RegisterWallTime(TimeSpan.FromSeconds(61));
        Assert.Equal(ForgeBudgetDimension.WallTime, wall.ExhaustedDimension());
    }

    [Fact]
    public void Consumption_accumulates_across_registrations()
    {
        var budget = new ForgeBudget { MaxTokens = 1000 };
        budget.RegisterTokens(400);
        budget.RegisterTokens(400);

        Assert.Null(budget.ExhaustedDimension());
        Assert.Equal(800, budget.ConsumedTokens);

        budget.RegisterTokens(400);
        Assert.Equal(ForgeBudgetDimension.Tokens, budget.ExhaustedDimension());
    }

    [Fact]
    public void Negative_wall_time_is_ignored_and_negative_tokens_are_refused()
    {
        var budget = new ForgeBudget();
        budget.RegisterWallTime(TimeSpan.FromSeconds(-5));
        Assert.Equal(0, budget.ConsumedWallSeconds);

        Assert.Throws<ArgumentOutOfRangeException>(() => budget.RegisterTokens(-1));
    }
}
