using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Task;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Application.Tests.Crew;

/// <summary>Characterisation: how the prompt lists a tool's optional parameters.</summary>
public class AgentPromptComposerToolParametersTests
{
    [Fact]
    [Trait("BR", "CH-02")]
    [Trait("BR", "CH-03")]
    public void ShouldListPayloadAndMetadataAsOptional_WhenPostMessageIsInThePrompt()
    {
        var agent = new AgentBuilder().Role("Analyst").Goal("Analyze").WithTool(new PostMessageStub()).Build();
        var task = new CrewTaskBuilder().Description("Do work").ExpectedOutput("Result").Build();

        var prompt = AgentPromptComposer.BuildSystemPrompt(
            agent, task, TaskToolbelt.Compose(agent, task), supportsNativeToolCalling: true);

        Assert.Contains("  Required: target_mailbox", prompt, StringComparison.Ordinal);
        Assert.Contains("  Optional: payload, metadata", prompt, StringComparison.Ordinal);
    }

    private sealed class PostMessageStub : IBaseTool
    {
        public string Name => "post_message";
        public string Description => "stub";
        public ToolSchema Schema { get; } = new("post_message", "stub", new Dictionary<string, ParameterSchema>
        {
            ["target_mailbox"] = new("string", "Mailbox", true),
            ["payload"] = new("any", "JSON payload", false),
            ["metadata"] = new("object", "Metadata", false)
        });

        public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(
            Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new ToolCallResponse(true, "stub result", null));

        public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(
            string input, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(ToolResult.CreateSuccess("stub result"));

        public bool ValidateInput(string input) => true;
    }
}
