using Orkeon.Application.Rag;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Knowledge;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Knowledge;

public class RagToolTestsFixture
{
    private readonly MockRagPipeline _mockRagPipeline = new();

    public RagToolTestsFixture WithPipelineResult(RagResult? result = null)
    {
        result ??= new RagResult
        {
            Answer = "The answer is 42.",
            Sources =
            [
                new()
                {
                    Content = "Source content about the answer.",
                    SourceId = "guide.txt",
                    RelevanceScore = 0.92f,
                    Metadata = []
                }
            ]
        };

        _mockRagPipeline.SetExecuteResult(result);
        return this;
    }

    public RagTool CreateTool() => new(_mockRagPipeline);

    public static Task<ToolCallResponse> CallAsync(RagTool tool, string toolName, Dictionary<string, object?> parameters)
        => tool.CallAsync(new ToolCallRequest(toolName, parameters));

    public static Task<ToolCallResponse> CallAsync(RagTool tool, ToolCallRequest request)
        => tool.CallAsync(request);

    public MockRagPipeline GetPipeline() => _mockRagPipeline;
}
