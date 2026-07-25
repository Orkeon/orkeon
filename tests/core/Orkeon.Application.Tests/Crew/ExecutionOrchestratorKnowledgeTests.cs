using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Rag.Abstractions.Interfaces;
using Orkeon.Rag.Abstractions.Models;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Application.Tests.Crew;

/// <summary>
/// Wiring tests for knowledge-context injection at prompt assembly time (RAG-03/C4):
/// an agent with <c>KnowledgeAttachments</c> and a resolved
/// <see cref="IKnowledgeContextAugmenter"/> gets the retrieved block (with numbered
/// citations) in its user prompt; every other combination leaves the prompt
/// byte-identical to the pre-RAG output.
/// </summary>
public class ExecutionOrchestratorKnowledgeTests
{
    private const string SampleBlockText =
        "## Knowledge Context\n\n" +
        "Answer from the excerpts below and cite the passages you use with their [n] markers.\n\n" +
        "[1] (collection: produits, source: faq.md, score: 0.91)\nRefunds are possible within 30 days.\n\n" +
        "[2] (collection: produits, source: catalogue.pdf, score: 0.72)\nThe Pro plan includes 5 seats.";

    private static KnowledgeContextBlock SampleBlock() => new()
    {
        Text = SampleBlockText,
        Citations =
        [
            new Citation { Marker = 1, ChunkId = "c1", SourceId = "faq.md", Score = 0.91 },
            new Citation { Marker = 2, ChunkId = "c2", SourceId = "catalogue.pdf", Score = 0.72 },
        ],
    };

    // ── Test doubles ────────────────────────────────────────────────────────

    /// <summary>Records every prompt the orchestrator sends to the LLM.</summary>
    private sealed class RecordingLlmProvider : IBasicLlmProvider
    {
        public string Name => "recording";
        public List<string> ReceivedMessages { get; } = [];

        public System.Threading.Tasks.Task<string> ChatAsync(
            string message, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            ReceivedMessages.Add(message);
            return System.Threading.Tasks.Task.FromResult("Final answer.");
        }

        public System.Threading.Tasks.Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(true);
    }

    private sealed class NullPlanner : Orkeon.Domain.Crew.Planning.IAgentPlanner
    {
        public System.Threading.Tasks.Task<Orkeon.Domain.Crew.Planning.TaskPlan> CreatePlanAsync(
            DomainTask task, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new Orkeon.Domain.Crew.Planning.TaskPlan());

        public System.Threading.Tasks.Task<Orkeon.Domain.Crew.Planning.TaskPlan> RefinePlanAsync(
            Orkeon.Domain.Crew.Planning.TaskPlan plan,
            Orkeon.Domain.Crew.Planning.PlanFeedback feedback,
            CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(plan);

        public System.Threading.Tasks.Task<Orkeon.Domain.Crew.Planning.PlanValidationResult> ValidatePlanAsync(
            Orkeon.Domain.Crew.Planning.TaskPlan plan, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(
                new Orkeon.Domain.Crew.Planning.PlanValidationResult { IsValid = true });
    }

    // ── Factories ───────────────────────────────────────────────────────────

    private static ExecutionOrchestrator CreateOrchestrator(
        RecordingLlmProvider llmProvider,
        IKnowledgeContextAugmenter? augmenter = null) =>
        new(NullLogger<ExecutionOrchestrator>.Instance, llmProvider, new NullPlanner())
        {
            KnowledgeAugmenter = augmenter,
        };

    private static DomainAgent CreateAgent(bool withKnowledge)
    {
        var builder = new AgentBuilder()
            .Role("Support agent")
            .Goal("Answer customer questions");
        if (withKnowledge)
            builder.WithKnowledge("produits", opts => opts.TopK = 3);
        return builder.Build();
    }

    private static DomainTask CreateTask() => DomainTask.Create(
        TaskDescription.From("Explain the refund policy for {product}."),
        ExpectedOutput.From("A short grounded answer."));

    private static SimpleExecutionContext CreateContext() => new(
        CrewId.From(Guid.NewGuid()),
        new Dictionary<string, string> { ["product"] = "Pro plan" },
        NullMemoryScope.Instance,
        [],
        CancellationToken.None);

    // ── Prompt composition (AgentPromptComposer) ────────────────────────────

    [Fact]
    public void BuildUserPrompt_WithKnowledgeContext_AppendsTheBlockAfterTaskContext()
    {
        var task = CreateTask();
        var context = CreateContext();

        var prompt = AgentPromptComposer.BuildUserPrompt(task, context, SampleBlockText);

        Assert.Contains("## Knowledge Context", prompt, StringComparison.Ordinal);
        Assert.Contains("[1] (collection: produits, source: faq.md, score: 0.91)", prompt, StringComparison.Ordinal);
        // The block comes after the task/context sections.
        Assert.True(prompt.IndexOf("## Knowledge Context", StringComparison.Ordinal)
            > prompt.IndexOf("Expected output:", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildUserPrompt_WithoutKnowledgeContext_IsByteIdenticalToTheLegacyOutput()
    {
        var task = CreateTask();
        var context = CreateContext();

        var legacy = AgentPromptComposer.BuildUserPrompt(task, context);
        var withNull = AgentPromptComposer.BuildUserPrompt(task, context, null);
        var withBlank = AgentPromptComposer.BuildUserPrompt(task, context, "   ");

        Assert.Equal(legacy, withNull);
        Assert.Equal(legacy, withBlank);
        Assert.DoesNotContain("## Knowledge Context", legacy, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildKnowledgeQueryText_IncludesInterpolatedDescriptionExpectedOutputAndVariables()
    {
        var task = CreateTask();
        var context = CreateContext();

        var query = AgentPromptComposer.BuildKnowledgeQueryText(task, context);

        Assert.Contains("Explain the refund policy for Pro plan.", query, StringComparison.Ordinal);
        Assert.Contains("A short grounded answer.", query, StringComparison.Ordinal);
        Assert.Contains("- product: Pro plan", query, StringComparison.Ordinal);
    }

    // ── Execution wiring (ExecutionOrchestrator) ────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task ExecuteTask_AgentWithAttachments_InjectsBlockWithCitationsIntoThePrompt()
    {
        var llm = new RecordingLlmProvider();
        var augmenter = new FakeKnowledgeContextAugmenter { BlockToReturn = SampleBlock() };
        var orchestrator = CreateOrchestrator(llm, augmenter);
        var agent = CreateAgent(withKnowledge: true);

        var result = await orchestrator.ExecuteTaskCoreAsync(
            agent, CreateTask(), CreateContext(), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var (attachments, taskInput) = Assert.Single(augmenter.Calls);
        Assert.Equal(agent.KnowledgeAttachments, attachments);
        Assert.Contains("Explain the refund policy for Pro plan.", taskInput, StringComparison.Ordinal);

        var prompt = Assert.Single(llm.ReceivedMessages);
        Assert.Contains("## Knowledge Context", prompt, StringComparison.Ordinal);
        Assert.Contains("[1] (collection: produits, source: faq.md, score: 0.91)", prompt, StringComparison.Ordinal);
        Assert.Contains("[2] (collection: produits, source: catalogue.pdf, score: 0.72)", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteTask_AgentWithoutAttachments_AugmenterNotCalled_PromptByteIdentical()
    {
        var agent = CreateAgent(withKnowledge: false);

        var llmWithout = new RecordingLlmProvider();
        await CreateOrchestrator(llmWithout).ExecuteTaskCoreAsync(
            agent, CreateTask(), CreateContext(), TestContext.Current.CancellationToken);

        var llmWith = new RecordingLlmProvider();
        var augmenter = new FakeKnowledgeContextAugmenter { BlockToReturn = SampleBlock() };
        await CreateOrchestrator(llmWith, augmenter).ExecuteTaskCoreAsync(
            agent, CreateTask(), CreateContext(), TestContext.Current.CancellationToken);

        Assert.Empty(augmenter.Calls);
        Assert.Equal(llmWithout.ReceivedMessages, llmWith.ReceivedMessages);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteTask_NoAugmenter_AgentWithAttachments_PromptByteIdentical()
    {
        var agent = CreateAgent(withKnowledge: true);

        var llmBaseline = new RecordingLlmProvider();
        await CreateOrchestrator(llmBaseline).ExecuteTaskCoreAsync(
            CreateAgent(withKnowledge: false), CreateTask(), CreateContext(), TestContext.Current.CancellationToken);

        var llm = new RecordingLlmProvider();
        await CreateOrchestrator(llm).ExecuteTaskCoreAsync(
            agent, CreateTask(), CreateContext(), TestContext.Current.CancellationToken);

        Assert.Equal(llmBaseline.ReceivedMessages, llm.ReceivedMessages);
        Assert.DoesNotContain("## Knowledge Context", Assert.Single(llm.ReceivedMessages), StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task ExecuteTask_AugmenterReturnsNull_PromptByteIdentical()
    {
        var agent = CreateAgent(withKnowledge: true);

        var llmWithout = new RecordingLlmProvider();
        await CreateOrchestrator(llmWithout).ExecuteTaskCoreAsync(
            agent, CreateTask(), CreateContext(), TestContext.Current.CancellationToken);

        var llmWith = new RecordingLlmProvider();
        var augmenter = new FakeKnowledgeContextAugmenter { BlockToReturn = null };
        await CreateOrchestrator(llmWith, augmenter).ExecuteTaskCoreAsync(
            agent, CreateTask(), CreateContext(), TestContext.Current.CancellationToken);

        Assert.Single(augmenter.Calls);
        Assert.Equal(llmWithout.ReceivedMessages, llmWith.ReceivedMessages);
    }

    // ── DI wiring (RegisterExecutionOrchestrator) ───────────────────────────

    [Fact]
    public void AddOrkeonApplication_WithAugmenterRegistered_WiresItOnTheOrchestrator()
    {
        var services = CreateServiceCollectionWithRequiredDeps();
        var augmenter = new FakeKnowledgeContextAugmenter();
        services.AddSingleton<IKnowledgeContextAugmenter>(augmenter);
        services.AddOrkeonApplication();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var orchestrator = Assert.IsType<ExecutionOrchestrator>(
            scope.ServiceProvider.GetRequiredService<IExecutionOrchestrator>());

        Assert.Same(augmenter, orchestrator.KnowledgeAugmenter);
    }

    [Fact]
    public void AddOrkeonApplication_WithoutAugmenter_LeavesTheOrchestratorUnaugmented()
    {
        var services = CreateServiceCollectionWithRequiredDeps();
        services.AddOrkeonApplication();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var orchestrator = Assert.IsType<ExecutionOrchestrator>(
            scope.ServiceProvider.GetRequiredService<IExecutionOrchestrator>());

        Assert.Null(orchestrator.KnowledgeAugmenter);
    }

    private static ServiceCollection CreateServiceCollectionWithRequiredDeps()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IBasicLlmProvider, RecordingLlmProvider>();
        services.AddScoped<Orkeon.Domain.Crew.Planning.IAgentPlanner, NullPlanner>();
        services.AddSingleton<Orkeon.Domain.FileSystem.IFileSystemService>(
            new Orkeon.Tests.Shared.FileSystem.FakeFileSystemService());
        return services;
    }
}
