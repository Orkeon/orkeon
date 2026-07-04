using Orkeon.Application.Evaluation;

namespace Orkeon.Infrastructure.Tests.Evaluation.LlmJudge;

public sealed class CoherenceEvaluatorTests : IDisposable
{
    private readonly CoherenceEvaluatorTestsFixture _fixture = new();

    [Fact]
    public void ShouldBeCoherence_WhenAccessingName()
    {
        var evaluator = _fixture.CreateEvaluator();

        Assert.Equal("Coherence", evaluator.Name);
    }

    [Fact]
    public void ShouldHaveDescription_WhenAccessingDescription()
    {
        var evaluator = _fixture.CreateEvaluator();

        Assert.NotNull(evaluator.Description);
        Assert.NotEmpty(evaluator.Description);
    }

    [Fact]
    public void ShouldRequireLlm_WhenAccessingRequiresLlm()
    {
        var evaluator = _fixture.CreateEvaluator();

        Assert.True(evaluator.RequiresLlm);
    }

    [Fact]
    public async Task ShouldIncludeOutputInPrompt_WhenBuilding()
    {
        var evaluator = _fixture
            .WithChatResponse("""{"score": 7, "reasoning": "Mostly coherent"}""")
            .CreateEvaluator();

        var input = new EvaluationInput(
            Output: "The sky is blue. Also, the sky is green.",
            TaskDescription: "Describe sky color");

        await evaluator.EvaluateAsync(input, TestContext.Current.CancellationToken);

        var chatClient = _fixture.GetChatClient();
        Assert.NotNull(chatClient.LastGetResponseMessages);
        var capturedPrompt = chatClient.LastGetResponseMessages!.Last().Text;
        Assert.NotNull(capturedPrompt);
        Assert.Contains("sky is blue", capturedPrompt);
        Assert.Contains("Describe sky color", capturedPrompt);
        Assert.Contains("coherence", capturedPrompt, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        GC.SuppressFinalize(this);
    }
}
