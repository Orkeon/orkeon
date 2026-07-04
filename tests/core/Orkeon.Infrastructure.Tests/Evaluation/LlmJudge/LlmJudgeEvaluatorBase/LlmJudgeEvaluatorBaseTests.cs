using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation.LlmJudge;

public class LlmJudgeEvaluatorBaseTests
{
    [Fact]
    public async Task ShouldExtractScoreAndReasoning_WhenResponseIsValidJson()
    {
        using var fixture = new LlmJudgeEvaluatorBaseTestsFixture();
        var evaluator = fixture
            .WithChatResponse("""{"score": 8, "reasoning": "Well structured and coherent."}""")
            .CreateEvaluator();

        var input = new EvaluationInput(Output: "test output");

        var result = await evaluator.EvaluateAsync(input, TestContext.Current.CancellationToken);

        Assert.Equal(0.8, result.Score);
        Assert.Equal("Well structured and coherent.", result.Reasoning);
    }

    [Fact]
    public async Task ShouldFallBackGracefully_WhenResponseIsMalformed()
    {
        using var fixture = new LlmJudgeEvaluatorBaseTestsFixture();
        var evaluator = fixture
            .WithChatResponse("I give this a 7/10 because it's mostly good.")
            .CreateEvaluator();

        var input = new EvaluationInput(Output: "test output");

        var result = await evaluator.EvaluateAsync(input, TestContext.Current.CancellationToken);

        Assert.Equal(0.7, result.Score);
    }

    [Fact]
    public async Task ShouldReturnNeutralScore_WhenResponseIsCompletelyUnparseable()
    {
        using var fixture = new LlmJudgeEvaluatorBaseTestsFixture();
        var evaluator = fixture
            .WithChatResponse("This output is okay.")
            .CreateEvaluator();

        var input = new EvaluationInput(Output: "test output");

        var result = await evaluator.EvaluateAsync(input, TestContext.Current.CancellationToken);

        Assert.Equal(0.5, result.Score);
        Assert.Contains("Could not parse", result.Reasoning);
    }

    [Fact]
    public async Task ShouldReturnScoreZero_WhenLlmThrowsException()
    {
        using var fixture = new LlmJudgeEvaluatorBaseTestsFixture();
        var evaluator = fixture
            .WithChatException(new InvalidOperationException("API error"))
            .CreateEvaluator();

        var input = new EvaluationInput(Output: "test output");

        var result = await evaluator.EvaluateAsync(input, TestContext.Current.CancellationToken);

        Assert.Equal(0.0, result.Score);
        Assert.Contains("LLM judge failed", result.Reasoning);
    }

    [Fact]
    public void ShouldRequireLlm_WhenAccessingRequiresLlm()
    {
        using var fixture = new LlmJudgeEvaluatorBaseTestsFixture();
        var evaluator = fixture.CreateEvaluator();

        Assert.True(evaluator.RequiresLlm);
    }

    [Fact]
    public async Task ShouldClampScoreBetweenZeroAndOne_WhenScoreExceedsRange()
    {
        using var fixture = new LlmJudgeEvaluatorBaseTestsFixture();
        var evaluator = fixture
            .WithChatResponse("""{"score": 15, "reasoning": "Extraordinary"}""")
            .CreateEvaluator();

        var input = new EvaluationInput(Output: "test output");

        var result = await evaluator.EvaluateAsync(input, TestContext.Current.CancellationToken);

        Assert.InRange(result.Score, 0.0, 1.0);
    }
}
