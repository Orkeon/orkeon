using Orkeon.Application.Evaluation;
using Microsoft.Extensions.AI;
using Orkeon.Infrastructure.Evaluation.LlmJudge;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Evaluation.LlmJudge;

public sealed class CoherenceEvaluatorTestsFixture : IDisposable
{
    private readonly MockChatClient _mockChatClient = new();

    public CoherenceEvaluatorTestsFixture WithChatResponse(string responseText)
    {
        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText));
        _mockChatClient.SetGetResponseResult(chatResponse);
        return this;
    }

    public CoherenceEvaluator CreateEvaluator()
        => new(_mockChatClient);

    public static Task<EvaluationScore> EvaluateAsync(CoherenceEvaluator evaluator, EvaluationInput input)
        => evaluator.EvaluateAsync(input);

    public MockChatClient GetChatClient() => _mockChatClient;

    public void Dispose()
    {
        _mockChatClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
