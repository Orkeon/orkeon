using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.HumanInput;
using Orkeon.Infrastructure.HumanInput;

namespace Orkeon.Infrastructure.Tests.HumanInput;

public sealed class ConsoleHumanInputProviderTests
{
    private static readonly string[] Expected = ["alpha", "gamma"];
    private static readonly string[] Options = ["alpha", "beta", "gamma"];

    private static HumanInputContext Context() =>
        new() { Prompt = "Pick", Options = Options, Recommended = 1 };

    [Fact]
    [Trait("BR", "RT-08")]
    public async Task ShouldMarkRecommendedOption_WhenConsoleRendersChoice()
    {
        using var input = new StringReader("2\n");
        using var output = new StringWriter();
        IHumanInputProvider provider = new ConsoleHumanInputProvider(NullLogger<ConsoleHumanInputProvider>.Instance, input, output);

        var answer = await provider.GetChoiceAsync(Context(), TestContext.Current.CancellationToken);

        Assert.Equal("beta", answer);
        var rendered = output.ToString();
        Assert.Contains("2. beta (recommended)", rendered);
        Assert.DoesNotContain("alpha (recommended)", rendered);
    }

    [Fact]
    [Trait("BR", "RT-08")]
    public async Task ShouldReturnList_WhenConsoleReadsMultiAnswer()
    {
        using var input = new StringReader("1, gamma\n");
        using var output = new StringWriter();
        IHumanInputProvider provider = new ConsoleHumanInputProvider(NullLogger<ConsoleHumanInputProvider>.Instance, input, output);

        var answer = await provider.GetMultiChoiceAsync(Context(), TestContext.Current.CancellationToken);

        Assert.Equal(Expected, answer);
    }
}
