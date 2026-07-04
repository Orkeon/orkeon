using Orkeon.Application.Evaluation;
using Microsoft.Extensions.AI;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Evaluation.LlmJudge;

namespace Orkeon.Infrastructure.Tests.Evaluation.LlmJudge;

public sealed class LlmJudgeEvaluatorBaseTestsFixture : IDisposable
{
    private readonly MockChatClient _mockChatClient = new();

    public LlmJudgeEvaluatorBaseTestsFixture WithChatResponse(string responseText)
    {
        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText));
        _mockChatClient.SetGetResponseResult(chatResponse);
        return this;
    }

    public LlmJudgeEvaluatorBaseTestsFixture WithChatException(Exception exception)
    {
        _mockChatClient.SetGetResponseException(exception);
        return this;
    }

    public TestJudgeEvaluator CreateEvaluator()
        => new(_mockChatClient);

    public static Task<EvaluationScore> EvaluateAsync(TestJudgeEvaluator evaluator, EvaluationInput input)
        => evaluator.EvaluateAsync(input);

    public MockChatClient GetChatClient() => _mockChatClient;

    /// <summary>
    /// Concrete test subclass of LlmJudgeEvaluatorBase for testing the base functionality.
    /// </summary>
    public sealed class TestJudgeEvaluator : LlmJudgeEvaluatorBase
    {
        public override string Name => "TestJudge";
        public override string Description => "Test judge evaluator.";

        public TestJudgeEvaluator(IChatClient chatClient) : base(chatClient) { }

        protected override string BuildJudgePrompt(EvaluationInput input)
        {
            return $"Evaluate this: {input.Output}" + JudgeInstructionSuffix;
        }
    }

    public void Dispose()
    {
        _mockChatClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
