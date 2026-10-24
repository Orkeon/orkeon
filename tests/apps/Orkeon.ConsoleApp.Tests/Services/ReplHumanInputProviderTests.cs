using Orkeon.ConsoleApp.Services;
using Orkeon.ConsoleApp.Tests.Fakes;
using Orkeon.Domain.HumanInput;

namespace Orkeon.ConsoleApp.Tests.Services;

/// <summary>The REPL's human_input provider: the question is written, the read loop's next line answers it.</summary>
public sealed class ReplHumanInputProviderTests
{
    private readonly FakeConsoleAdapter _adapter = new();
    private readonly ReplHumanInputProvider _provider;
    private int _factoryCalls;

    public ReplHumanInputProviderTests() => _provider = new ReplHumanInputProvider(() =>
    {
        _factoryCalls++;
        return _adapter;
    });

    private string Written => string.Join("\n", _adapter.Lines);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HumanInputContext Choice(bool multi = false, int? recommended = 1) => new()
    {
        Prompt = "Which database?",
        InputType = "choice",
        Options = ["sqlite", "postgres", "mysql"],
        Recommended = recommended,
        Multi = multi,
    };

    [Fact]
    [Trait("BR", "RT-08")]
    public async Task ShouldReadAnswerThroughInterceptor_WhenChoiceAsked()
    {
        var answer = _provider.GetChoiceAsync(Choice(), Ct);

        Assert.False(answer.IsCompleted);
        Assert.Contains("Which database?", Written, StringComparison.Ordinal);
        Assert.Contains("postgres (recommended)", Written, StringComparison.Ordinal);
        Assert.NotNull(_provider.PromptOverride);
        Assert.True(_provider.TryConsume("3"));

        Assert.Equal("mysql", await answer);
        Assert.Null(_provider.PromptOverride);
    }

    [Fact]
    [Trait("BR", "RT-08")]
    public async Task ShouldWriteQuestionThroughConsoleAdapter_WhenChoiceAsked()
    {
        var answer = _provider.GetChoiceAsync(Choice(), Ct);

        Assert.Equal(
            ["Which database?", "  1. sqlite", "  2. postgres (recommended)", "  3. mysql"],
            _adapter.Lines);
        Assert.True(_provider.TryConsume("1"));
        Assert.Equal("sqlite", await answer);
    }

    [Fact]
    [Trait("BR", "RT-08")]
    public async Task ShouldResolveAdapterAtFirstQuestion_WhenProviderBuiltBeforeAdapterIsReplaced()
    {
        Assert.Equal(0, _factoryCalls);

        var answer = _provider.GetChoiceAsync(Choice(), Ct);

        Assert.True(_factoryCalls >= 1);
        Assert.True(_provider.TryConsume("1"));
        Assert.Equal("sqlite", await answer);
    }

    [Fact]
    [Trait("BR", "RT-08")]
    public async Task ShouldReturnList_WhenMultiAsked()
    {
        var answer = _provider.GetMultiChoiceAsync(Choice(multi: true), Ct);

        Assert.True(_provider.TryConsume("1, mysql"));

        Assert.Equal(["sqlite", "mysql"], await answer);
    }

    [Fact]
    [Trait("BR", "RT-08")]
    public async Task ShouldTakeRecommended_WhenAnswerIsEmpty()
    {
        var answer = _provider.GetChoiceAsync(Choice(), Ct);

        Assert.True(_provider.TryConsume(""));

        Assert.Equal("postgres", await answer);
    }
}
