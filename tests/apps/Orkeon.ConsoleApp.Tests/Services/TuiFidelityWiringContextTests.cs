using Orkeon.ConsoleApp.Services;

namespace Orkeon.ConsoleApp.Tests.Services;

public sealed class TuiFidelityWiringContextTests
{
    [Theory]
    [InlineData(50_000, 200_000, 25)]
    [InlineData(300_000, 200_000, 100)]
    [InlineData(1, 200_000, 0)]
    public void The_context_is_the_share_of_the_window_that_the_last_prompt_filled(long tokens, long window, int expected)
    {
        // Act
        var percent = TuiFidelityWiring.ContextPercent(tokens, window);

        // Assert
        Assert.Equal(expected, percent);
    }

    [Theory]
    [InlineData(0, 200_000)]
    [InlineData(50_000, 0)]
    public void An_unknown_figure_gives_no_context(long tokens, long window)
    {
        // Act
        var percent = TuiFidelityWiring.ContextPercent(tokens, window);

        // Assert
        Assert.Null(percent);
    }
}
