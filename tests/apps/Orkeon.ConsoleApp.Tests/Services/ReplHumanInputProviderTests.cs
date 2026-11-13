using Orkeon.Cli.Abstractions.Console;
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

    private static ReplHumanInputProvider AskedByTask(IConsoleAdapter console)
        => new(() => console, () => ("t8", "dev-spec"));

    [Fact]
    public async Task ShouldTagQuestionAsAttention_WhenBackgroundTaskAsksInThreadedConsole()
    {
        var console = new FakeThreadedConsole { SeparatesThreads = true };
        var provider = AskedByTask(console);

        var answer = provider.GetChoiceAsync(Choice(), Ct);

        Assert.Equal(
            [("t8", "[t8] dev-spec asks: Which database?"), ("t8", "  1. sqlite"), ("t8", "  2. postgres (recommended)"), ("t8", "  3. mysql")],
            console.Attentions);
        Assert.Empty(console.PlainLines);
        Assert.Equal("answer [t8]> ", provider.PromptOverride);
        Assert.True(provider.TryConsume("2"));
        Assert.Equal("postgres", await answer);
    }

    [Fact]
    public async Task ShouldWriteOneTaggedLine_WhenBackgroundTaskAsksInPlainConsole()
    {
        var provider = AskedByTask(_adapter);

        var answer = provider.GetInputAsync(new HumanInputContext { Prompt = "Name?", InputType = "text" }, Ct);

        Assert.Equal("[t8] dev-spec asks: Name?", Assert.Single(_adapter.Lines));
        Assert.True(provider.TryConsume("Ada"));
        Assert.Equal("Ada", await answer);
        Assert.Equal("[t8] answer taken: Ada", _adapter.Lines[^1]);
    }

    [Fact]
    public async Task ShouldTakeAnswerFromTaskThread_WhenThatTaskAsked()
    {
        var console = new FakeThreadedConsole { SeparatesThreads = true };
        var provider = AskedByTask(console);
        var answer = provider.GetChoiceAsync(Choice(), Ct);

        Assert.False(provider.TryAnswer("t9", "1"));
        Assert.False(answer.IsCompleted);
        Assert.True(provider.TryAnswer("t8", " 3 "));

        Assert.Equal("mysql", await answer);
        Assert.Null(provider.PromptOverride);
        Assert.Equal(("t8", "[t8] answer taken: 3"), console.Attentions[^1]);
    }

    [Fact]
    public async Task ShouldKeepQuestionOpenAndSayWhy_WhenTaskThreadAnswerIsInvalid()
    {
        var console = new FakeThreadedConsole { SeparatesThreads = true };
        var provider = AskedByTask(console);
        var answer = provider.GetChoiceAsync(Choice(), Ct);

        Assert.True(provider.TryAnswer("t8", "oracle"));

        Assert.False(answer.IsCompleted);
        Assert.Equal(("t8", "Pick a number or a value from the list."), console.Attentions[^1]);
        Assert.True(provider.TryAnswer("t8", "sqlite"));
        Assert.Equal("sqlite", await answer);
    }

    [Fact]
    public void ShouldRefuseTaskThreadAnswer_WhenNoQuestionWaits()
        => Assert.False(AskedByTask(_adapter).TryAnswer("t8", "1"));

    [Fact]
    public async Task ShouldRaisePendingChanged_WhenQuestionAskedAnsweredOrWithdrawn()
    {
        var provider = AskedByTask(_adapter);
        var raised = 0;
        provider.PendingChanged += (_, _) => raised++;

        var answered = provider.GetChoiceAsync(Choice(), Ct);
        Assert.Equal(1, raised);
        Assert.True(provider.TryConsume("1"));
        await answered;
        Assert.Equal(2, raised);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var withdrawn = provider.GetChoiceAsync(Choice(), cts.Token);
        Assert.Equal(3, raised);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => withdrawn);
        Assert.Equal(4, raised);
        Assert.Null(provider.PromptOverride);
    }

    [Fact]
    public async Task ShouldKeepPlainPrompt_WhenMainThreadAsks()
    {
        var answer = _provider.GetChoiceAsync(Choice(), Ct);

        Assert.Equal("answer> ", _provider.PromptOverride);
        Assert.True(_provider.TryConsume("1"));
        await answer;
        Assert.DoesNotContain(_adapter.Lines, l => l.Contains("answer taken", StringComparison.Ordinal));
    }
}
