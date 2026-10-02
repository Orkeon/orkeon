using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// The correction round <see cref="OutputValidationCoordinator"/> runs without a chat client:
/// one call to the basic provider, on the agent's configuration.
/// </summary>
public sealed class OutputValidationCoordinatorTests
{
    // GAP-18: an agent without an LLM config had its correction asked of OpenAI's default model,
    // whatever vendor the provider is; the call names no model now, so the provider sends its own.
    [Fact]
    public async System.Threading.Tasks.Task The_single_shot_correction_of_an_agent_without_an_llm_config_names_no_model()
    {
        var provider = new ScriptedBasicLlmProvider { FallbackResponse = "{\"fixed\": true}" };
        var coordinator = new OutputValidationCoordinator(
            new SpyExecutionLogger(), new ScriptedValidationPipeline(false, true), parserFactory: null,
            provider, chatLoop: null);
        var agent = new AgentBuilder().Role("Writer").Goal("Write JSON").Build();
        var task = DomainTask.Create(TaskDescription.From("Write JSON"), ExpectedOutput.From("A JSON object"));
        var validation = new OutputValidationContext(ExpectedFormat: OutputFormat.Json);

        var (output, _) = await coordinator.ValidateAndParseOutputAsync(
            new OutputValidationRequest("not json", validation, task, agent, "system", "user", []),
            maxOutputRetries: 1, defaultMaxIterations: 5, TestContext.Current.CancellationToken);

        Assert.Equal("{\"fixed\": true}", output);
        Assert.Null(agent.LlmConfig);
        Assert.Equal(string.Empty, Assert.Single(provider.ReceivedConfigs)!.Model);
    }

    /// <summary>Answers the validation verdicts it was given, in order.</summary>
    private sealed class ScriptedValidationPipeline(params bool[] valid) : IOutputValidationPipeline
    {
        private int _next;

        public System.Threading.Tasks.Task<OutputPipelineResult> ValidateAsync(
            string output, OutputValidationContext context, CancellationToken ct = default)
        {
            var isValid = valid[Math.Min(_next++, valid.Length - 1)];
            return System.Threading.Tasks.Task.FromResult(isValid
                ? new OutputPipelineResult(true, [])
                : new OutputPipelineResult(false, [new OutputValidationResult(false, "not valid JSON", "Wrap it in braces", "scripted")], "not valid JSON"));
        }

        public IOutputValidationPipeline AddValidator(IOutputValidator validator) => this;
    }
}
