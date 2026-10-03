using Microsoft.Extensions.AI;
using Orkeon.Application.Context;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Task;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Tools.Protocol;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Orkeon.Infrastructure.Crew;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;
using AppTaskOutput = Orkeon.Application.Execution.TaskOutput;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Infrastructure.Tests.Managers;

public sealed class LlmBasedManagerTests : IDisposable
{
    private static readonly string[] s_codeTools = ["CodeGenerator", "Formatter"];

    private readonly TestManagerChatClient _llmProvider;
    private readonly ManagerLlm _llm;
    private readonly TestLogger<LlmBasedManager> _logger;
    private readonly LlmBasedManager _manager;

    public LlmBasedManagerTests()
    {
        _llmProvider = new TestManagerChatClient();
        // The LLM a crew gives its manager (GAP-19): the manager has none of its own.
        _llm = new ManagerLlm { ChatClient = _llmProvider, Name = "profile:default" };
        _logger = new TestLogger<LlmBasedManager>();
        _manager = new LlmBasedManager(_logger);
    }

    public void Dispose() => _llmProvider.Dispose();

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructorWithNullLogger()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new LlmBasedManager(null!));
        Assert.Equal("logger", exception.ParamName);
    }

    [Fact]
    public async Task ShouldThrowArgumentNullException_WhenAskedWithoutTheCrewsLlm()
    {
        // GAP-19: every call names the LLM the crew gives its manager — there is no default to fall back on.
        var task = CreateTestTask("Write unit tests");

        var assign = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.AssignTaskAsync(task, CreateTestAgents(), CreateTestContext(), null!));
        var review = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.ReviewOutputAsync(CreateTestOutput("Done"), task, null!, TestContext.Current.CancellationToken));

        Assert.Equal("llm", assign.ParamName);
        Assert.Equal("llm", review.ParamName);
    }

    [Fact]
    public async Task ShouldAskForTheModelTheCrewNames_AndNoneWhenItNamesNone()
    {
        var task = CreateTestTask("Write unit tests");
        _llmProvider.SetupResponse(JsonSerializer.Serialize(new { approved = true }));

        await _manager.ReviewOutputAsync(CreateTestOutput("Done"), task, _llm, TestContext.Current.CancellationToken);
        Assert.Null(_llmProvider.LastOptions);

        await _manager.ReviewOutputAsync(CreateTestOutput("Done"), task, new ManagerLlm
        {
            ChatClient = _llmProvider,
            Model = "chef-model",
            Name = "profile:b",
        }, TestContext.Current.CancellationToken);
        Assert.Equal("chef-model", _llmProvider.LastOptions!.ModelId);
    }

    [Fact]
    public async Task ShouldAssignToSelectedAgent_WhenAssignTaskAsyncWithValidResponse()
    {
        // Arrange
        var task = CreateTestTask("Write unit tests");
        var agents = CreateTestAgents();
        var context = CreateTestContext();

        var expectedAgentId = agents[1].Id.ToString();
        _llmProvider.SetupResponse(JsonSerializer.Serialize(new
        {
            agent_id = expectedAgentId,
            reason = "Developer has testing expertise"
        }));

        // Act
        var assignment = await _manager.AssignTaskAsync(task, agents, context, _llm);

        // Assert
        Assert.NotNull(assignment);
        Assert.Equal(task.Id, assignment.TaskId);
        Assert.Equal(agents[1].Id, assignment.AssignedAgent);
        Assert.Equal("Developer has testing expertise", assignment.Reason);
        Assert.True(_logger.HasLoggedInformation("Manager assigning task"));
        Assert.True(_logger.HasLoggedInformation("Task"));
    }

    [Fact]
    public async Task ShouldUseFallbackHeuristic_WhenAssignTaskAsyncWithInvalidJsonResponse()
    {
        // Arrange
        var task = CreateTestTask(GoalAnalyzeData);
        var agents = CreateTestAgents();
        var context = CreateTestContext();

        _llmProvider.SetupResponse("This is not valid JSON");

        // Act
        var assignment = await _manager.AssignTaskAsync(task, agents, context, _llm);

        // Assert
        Assert.NotNull(assignment);
        Assert.Equal(task.Id, assignment.TaskId);
        Assert.NotNull(assignment.AssignedAgent);
        Assert.Contains("Selected based on role and capability matching", assignment.Reason);
    }

    [Fact]
    public async Task ShouldAssignToFirstAgent_WhenAssignTaskAsyncWithLlmProviderException()
    {
        // Arrange
        var task = CreateTestTask("Debug code");
        var agents = CreateTestAgents();
        var context = CreateTestContext();

        _llmProvider.SetupException(new InvalidOperationException("LLM service unavailable"));

        // Act
        var assignment = await _manager.AssignTaskAsync(task, agents, context, _llm);

        // Assert
        Assert.NotNull(assignment);
        Assert.Equal(task.Id, assignment.TaskId);
        Assert.Equal(agents.First().Id, assignment.AssignedAgent);
        Assert.Equal("Assigned by fallback due to error in LLM assignment", assignment.Reason);
        Assert.True(_logger.HasLoggedError("Error during task assignment"));
    }

    [Fact]
    public async Task ShouldUseFallbackHeuristic_WhenAssignTaskAsyncWithNonExistentAgentId()
    {
        // Arrange
        var task = CreateTestTask("Review code");
        var agents = CreateTestAgents();
        var context = CreateTestContext();

        _llmProvider.SetupResponse(JsonSerializer.Serialize(new
        {
            agent_id = "non-existent-id",
            reason = "Some reason"
        }));

        // Act
        var assignment = await _manager.AssignTaskAsync(task, agents, context, _llm);

        // Assert
        Assert.NotNull(assignment);
        Assert.Equal(task.Id, assignment.TaskId);
        Assert.NotNull(assignment.AssignedAgent);
        Assert.Contains("Selected based on role and capability matching", assignment.Reason);
    }

    [Fact]
    public async Task ShouldReturnTrue_WhenReviewOutputAsyncWithApprovedResponse()
    {
        // Arrange
        var task = CreateTestTask("Generate report");
        var output = CreateTestOutput("Report generated successfully");

        _llmProvider.SetupResponse(JsonSerializer.Serialize(new
        {
            approved = true,
            feedback = "Good work"
        }));

        // Act
        var result = await _manager.ReviewOutputAsync(output, task, _llm, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.True(_logger.HasLoggedInformation("Manager reviewing output"));
        Assert.True(_logger.HasLoggedInformation("Output review"));
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenReviewOutputAsyncWithRejectedResponse()
    {
        // Arrange
        var task = CreateTestTask("Calculate metrics");
        var output = CreateTestOutput("Incomplete calculations");

        _llmProvider.SetupResponse(JsonSerializer.Serialize(new
        {
            approved = false,
            feedback = "Missing key metrics"
        }));

        // Act
        var result = await _manager.ReviewOutputAsync(output, task, _llm, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
        Assert.True(_logger.HasLoggedInformation("Output review"));
    }

    [Fact]
    public async Task ShouldParseKeywords_WhenReviewOutputAsyncWithInvalidJsonResponse()
    {
        // Arrange
        var task = CreateTestTask("Write documentation");
        var output = CreateTestOutput("Documentation complete");

        _llmProvider.SetupResponse("The output looks good and is approved");

        // Act
        var result = await _manager.ReviewOutputAsync(output, task, _llm, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result); // Should return true because "approved" is in the response

        // This test expects a warning to be logged when JSON parsing fails
        // Verification is that it falls back to keyword parsing and returns true
        // No need to check logs
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenReviewOutputAsyncWithNegativeKeywords()
    {
        // Arrange
        var task = CreateTestTask("Fix bugs");
        var output = CreateTestOutput("Partial fix applied");

        _llmProvider.SetupResponse("The output is rejected and needs more work");

        // Act
        var result = await _manager.ReviewOutputAsync(output, task, _llm, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldDefaultToApproval_WhenReviewOutputAsyncWithLlmProviderException()
    {
        // Arrange
        var task = CreateTestTask("Deploy application");
        var output = CreateTestOutput("Deployment complete");

        _llmProvider.SetupException(new InvalidOperationException("LLM timeout"));

        // Act
        var result = await _manager.ReviewOutputAsync(output, task, _llm, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.True(_logger.HasLoggedError("Error during output review"));
    }


    [Fact]
    public async Task ShouldIncludeTaskContextInPrompt_WhenAssignTaskAsync()
    {
        // Arrange
        var task = CreateTestTask("Process data");
        // Note: UpdateContext method doesn't exist, using Context property directly
        var agents = CreateTestAgents();
        var context = CreateTestContext();

        _llmProvider.SetupResponse(JsonSerializer.Serialize(new
        {
            agent_id = agents[0].Id.ToString(),
            reason = "Best fit"
        }));

        // Act
        await _manager.AssignTaskAsync(task, agents, context, _llm);

        // Assert
        var prompt = _llmProvider.GetLastPrompt();
        Assert.Contains("Context: Additional context information", prompt);
    }

    [Fact]
    public async Task ShouldIncludeAgentToolsInPrompt_WhenAssignTaskAsync()
    {
        // Arrange
        var task = CreateTestTask("Analyze code");
        var agents = CreateTestAgentsWithTools();
        var context = CreateTestContext();

        _llmProvider.SetupResponse(JsonSerializer.Serialize(new
        {
            agent_id = agents[0].Id.ToString(),
            reason = "Has required tools"
        }));

        // Act
        await _manager.AssignTaskAsync(task, agents, context, _llm);

        // Assert
        var prompt = _llmProvider.GetLastPrompt();
        Assert.Contains("Tools:", prompt);
        Assert.Contains("CodeAnalyzer", prompt);
    }

    [Fact]
    public async Task ShouldIncludeToolsUsedInPrompt_WhenReviewOutputAsync()
    {
        // Arrange
        var task = CreateTestTask("Generate code");
        var output = CreateTestOutputWithTools("Code generated", s_codeTools);

        _llmProvider.SetupResponse(JsonSerializer.Serialize(new
        {
            approved = true,
            feedback = ""
        }));

        // Act
        await _manager.ReviewOutputAsync(output, task, _llm, TestContext.Current.CancellationToken);

        // Assert
        var prompt = _llmProvider.GetLastPrompt();
        Assert.Contains("Tools Used: 2", prompt);
    }

    [Fact]
    public async Task ShouldGetHigherScore_WhenAssignTaskAsyncWithAgentDelegationCapability()
    {
        // Arrange
        var task = CreateTestTask("Manage project");
        var agents = new List<DomainAgent>
        {
            CreateAgent(RoleDeveloper, GoalWriteCode, false),
            CreateAgent(RoleManager, "Oversee projects", true)
        };
        var context = CreateTestContext();

        _llmProvider.SetupResponse("Invalid response to force fallback");

        // Act
        var assignment = await _manager.AssignTaskAsync(task, agents, context, _llm);

        // Assert
        // Manager agent should be selected due to delegation capability
        Assert.Equal(agents[1].Id, assignment.AssignedAgent);
    }

    [Fact]
    public async Task ShouldLogAssignmentDetails_WhenAssignTaskAsync()
    {
        // Arrange
        var task = CreateTestTask("Test task");
        var agents = CreateTestAgents();
        var context = CreateTestContext();

        var agentId = agents[0].Id.ToString();
        _llmProvider.SetupResponse(JsonSerializer.Serialize(new
        {
            agent_id = agentId,
            reason = "Test reason"
        }));

        // Act
        await _manager.AssignTaskAsync(task, agents, context, _llm);

        // Assert
        Assert.True(_logger.HasLoggedInformation("assigned to agent"));
        Assert.True(_logger.HasLoggedInformation("Test reason"));
    }

    [Fact]
    public async Task ShouldThrow_WhenAssignTaskAsyncWithEmptyAgentsList()
    {
        // Arrange
        var task = CreateTestTask("Test task");
        var agents = new List<DomainAgent>(); // Empty list
        var context = CreateTestContext();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.AssignTaskAsync(task, agents, context, _llm));
    }

    [Fact]
    public async Task ShouldThrow_WhenAssignTaskAsyncWithNullTask()
    {
        // Arrange
        DomainTask? task = null;
        var agents = CreateTestAgents();
        var context = CreateTestContext();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.AssignTaskAsync(task!, agents, context, _llm));
    }

    [Fact]
    public async Task ShouldThrow_WhenAssignTaskAsyncWithNullAgents()
    {
        // Arrange
        var task = CreateTestTask("Test task");
        List<DomainAgent>? agents = null;
        var context = CreateTestContext();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.AssignTaskAsync(task, agents!, context, _llm));
    }

    [Fact]
    public async Task ShouldNotThrow_WhenAssignTaskAsyncWithNullContext()
    {
        // Arrange
        var task = CreateTestTask("Test task");
        var agents = CreateTestAgents();
        SimpleExecutionContext? context = null;

        _llmProvider.SetupResponse(JsonSerializer.Serialize(new
        {
            agent_id = agents[0].Id.ToString(),
            reason = "Test"
        }));

        // Act
        var result = await _manager.AssignTaskAsync(task, agents, context!, _llm);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ShouldThrow_WhenReviewOutputAsyncWithNullOutput()
    {
        // Arrange
        AppTaskOutput? output = null;
        var task = CreateTestTask("Test task");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.ReviewOutputAsync(output!, task, _llm, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldThrow_WhenReviewOutputAsyncWithNullTask()
    {
        // Arrange
        var output = CreateTestOutput("Test output");
        DomainTask? task = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _manager.ReviewOutputAsync(output, task!, _llm, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ShouldUseFallback_WhenAssignTaskAsyncWithJsonMissingAgentId()
    {
        // Arrange
        var task = CreateTestTask("Test task");
        var agents = CreateTestAgents();
        var context = CreateTestContext();

        _llmProvider.SetupResponse(JsonSerializer.Serialize(new
        {
            // Missing agent_id field
            reason = "Some reason"
        }));

        // Act
        var assignment = await _manager.AssignTaskAsync(task, agents, context, _llm);

        // Assert
        Assert.NotNull(assignment);
        Assert.Contains("Selected based on role and capability matching", assignment.Reason);
    }

    [Fact]
    public async Task ShouldProvideDefaultReason_WhenAssignTaskAsyncWithJsonMissingReason()
    {
        // Arrange
        var task = CreateTestTask("Test task");
        var agents = CreateTestAgents();
        var context = CreateTestContext();

        _llmProvider.SetupResponse(JsonSerializer.Serialize(new
        {
            agent_id = agents[0].Id.ToString()
            // Missing reason field
        }));

        // Act
        var assignment = await _manager.AssignTaskAsync(task, agents, context, _llm);

        // Assert
        Assert.NotNull(assignment);
        Assert.Equal("No reason provided", assignment.Reason);
    }

    [Fact]
    public async Task ShouldUseFallback_WhenReviewOutputAsyncWithJsonMissingApproved()
    {
        // Arrange
        var task = CreateTestTask("Test task");
        var output = CreateTestOutput("Test output");

        _llmProvider.SetupResponse(JsonSerializer.Serialize(new
        {
            // Missing approved field
            feedback = "Some feedback"
        }));

        // Act
        var result = await _manager.ReviewOutputAsync(output, task, _llm, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result); // Should fall back to keyword parsing, which returns false
    }

    [Fact]
    public async Task ShouldParseFirst_WhenAssignTaskAsyncWithMultipleJsonBlocks()
    {
        // Arrange
        var task = CreateTestTask("Test task");
        var agents = CreateTestAgents();
        var context = CreateTestContext();

        var response = "Some text before\n" +
                      JsonSerializer.Serialize(new { agent_id = agents[0].Id.ToString(), reason = "First" }) +
                      "\nMore text\n" +
                      JsonSerializer.Serialize(new { agent_id = agents[1].Id.ToString(), reason = "Second" });

        _llmProvider.SetupResponse(response);

        // Act
        var assignment = await _manager.AssignTaskAsync(task, agents, context, _llm);

        // Assert
        Assert.Equal(agents[0].Id, assignment.AssignedAgent);
        Assert.Equal("First", assignment.Reason);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenReviewOutputAsyncWithKeywordReject()
    {
        // Arrange
        var task = CreateTestTask("Test task");
        var output = CreateTestOutput("Test output");

        _llmProvider.SetupResponse("The output is rejected and needs improvement");

        // Act
        var result = await _manager.ReviewOutputAsync(output, task, _llm, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldReturnFalse_WhenReviewOutputAsyncWithKeywordInSufficient()
    {
        // Arrange
        var task = CreateTestTask("Test task");
        var output = CreateTestOutput("Test output");

        _llmProvider.SetupResponse("The output is insufficient for the requirements");

        // Act
        var result = await _manager.ReviewOutputAsync(output, task, _llm, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldGetHigherScore_WhenAssignTaskAsyncWithAgentHavingMatchingRole()
    {
        // Arrange
        var task = CreateTestTask("Develop software feature");
        var agents = new List<DomainAgent>
        {
            CreateAgent("Tester", "Test software", false),
            CreateAgent(RoleDeveloper, "Develop features", false),
            CreateAgent("Designer", "Design interfaces", false)
        };
        var context = CreateTestContext();

        _llmProvider.SetupResponse("This is not valid JSON to force fallback");

        // Act
        var assignment = await _manager.AssignTaskAsync(task, agents, context, _llm);

        // Assert
        // Developer should be selected due to word matching
        Assert.Equal(agents[1].Id, assignment.AssignedAgent);
    }

    [Fact]
    public async Task ShouldThrow_WhenAssignTaskAsyncWithCancelledToken()
    {
        // Arrange
        var task = CreateTestTask("Test task");
        var agents = CreateTestAgents();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        using var memoryScope = new TestMemoryScope();
        var context = new SimpleExecutionContext(
            CrewId.From(Guid.NewGuid()),
            new Dictionary<string, string> { { "test", "value" } },
            memoryScope,
            [],
            cts.Token
        );

        _llmProvider.SetupException(new OperationCanceledException());

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _manager.AssignTaskAsync(task, agents, context, _llm));
    }

    [Fact]
    public async Task ShouldStop_WhenTheRunIsCancelledDuringAReview()
    {
        // GAP-38: the review takes the run's token — it went out under CancellationToken.None — and a
        // cancelled run is never an approval by default.
        var task = CreateTestTask("Test task");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _llmProvider.SetupException(new OperationCanceledException(cts.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _manager.ReviewOutputAsync(CreateTestOutput("Done"), task, _llm, cts.Token));
    }

    [Fact]
    public async Task ShouldDefaultToApproval_WhenAReviewTimesOutWhileTheRunGoesOn()
    {
        // A cancellation that is not the run's — a timeout the client surfaced — stays a failed
        // review, approved by default like any other.
        var task = CreateTestTask("Test task");
        _llmProvider.SetupException(new OperationCanceledException());

        var result = await _manager.ReviewOutputAsync(CreateTestOutput("Done"), task, _llm, TestContext.Current.CancellationToken);

        Assert.True(result);
    }

    [Fact]
    public async Task ShouldStillReview_WhenReviewOutputAsyncWithEmptyContent()
    {
        // Arrange
        var task = CreateTestTask("Test task");
        var output = CreateTestOutput("");

        _llmProvider.SetupResponse(JsonSerializer.Serialize(new
        {
            approved = false,
            feedback = "Output is empty"
        }));

        // Act
        var result = await _manager.ReviewOutputAsync(output, task, _llm, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ShouldIncludeInPrompt_WhenAssignTaskAsyncWithVeryLongTaskDescription()
    {
        // Arrange
        var longDescription = string.Join(" ", Enumerable.Repeat("complex task", 70)); // 70 * 13 = 910 chars, under 1000 limit
        var task = CreateTestTask(longDescription);
        var agents = CreateTestAgents();
        var context = CreateTestContext();

        _llmProvider.SetupResponse(JsonSerializer.Serialize(new
        {
            agent_id = agents[0].Id.ToString(),
            reason = "Test"
        }));

        // Act
        await _manager.AssignTaskAsync(task, agents, context, _llm);

        // Assert
        var prompt = _llmProvider.GetLastPrompt();
        Assert.Contains("complex task", prompt);
    }

    [Fact]
    public async Task ShouldIncludeInPrompt_WhenReviewOutputAsyncWithFailedTaskStatus()
    {
        // Arrange
        var task = CreateTestTask("Test task");
        var output = new AppTaskOutput(
            TaskId: Guid.NewGuid().ToString(),
            AgentId: Guid.NewGuid().ToString(),
            Content: "Failed output",
            CompletedAt: DateTime.UtcNow,
            Success: false, // Failed status
            ExecutionTime: TimeSpan.FromSeconds(5),
            ToolsUsed: []
        );

        _llmProvider.SetupResponse(JsonSerializer.Serialize(new
        {
            approved = false,
            feedback = "Task failed"
        }));

        // Act
        await _manager.ReviewOutputAsync(output, task, _llm, TestContext.Current.CancellationToken);

        // Assert
        var prompt = _llmProvider.GetLastPrompt();
        Assert.Contains("Success Status: False", prompt);
    }

    [Fact]
    public async Task ShouldAssignToThatAgent_WhenAssignTaskAsyncWithSingleAgent()
    {
        // Arrange
        var task = CreateTestTask("Test task");
        var agents = new List<DomainAgent> { CreateAgent("Solo", "Do everything", false) };
        var context = CreateTestContext();

        _llmProvider.SetupResponse("Invalid response");

        // Act
        var assignment = await _manager.AssignTaskAsync(task, agents, context, _llm);

        // Assert
        Assert.Equal(agents[0].Id, assignment.AssignedAgent);
    }

    [Fact]
    public async Task ShouldReturnTrue_WhenReviewOutputAsyncWithKeywordSatisfactory()
    {
        // Arrange
        var task = CreateTestTask("Test task");
        var output = CreateTestOutput("Good output");

        _llmProvider.SetupResponse("The output is satisfactory and complete");

        // Act
        var result = await _manager.ReviewOutputAsync(output, task, _llm, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ShouldReturnTrue_WhenReviewOutputAsyncWithKeywordMeetsRequirements()
    {
        // Arrange
        var task = CreateTestTask("Test task");
        var output = CreateTestOutput("Complete output");

        _llmProvider.SetupResponse("The output meets requirements perfectly");

        // Act
        var result = await _manager.ReviewOutputAsync(output, task, _llm, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ShouldGetBonusScore_WhenAssignTaskAsyncWithAgentWithManyTools()
    {
        // Arrange
        var task = CreateTestTask("Build something");
        var agentWithTools = CreateTestAgentsWithTools()[0];
        var agentWithoutTools = CreateAgent("Builder", "Build things", false);
        var agents = new List<DomainAgent> { agentWithoutTools, agentWithTools };
        var context = CreateTestContext();

        _llmProvider.SetupResponse("This is not valid JSON to force fallback");

        // Act
        var assignment = await _manager.AssignTaskAsync(task, agents, context, _llm);

        // Assert
        // Agent with tools should get bonus score
        Assert.Equal(agentWithTools.Id, assignment.AssignedAgent);
    }

    // Helper methods
    private static DomainTask CreateTestTask(string description)
    {
        var task = new CrewTaskBuilder()
            .Description(description)
            .ExpectedOutput("Expected output")
            .Build();

        return task;
    }

    private static AppTaskOutput CreateTestOutput(string content)
    {
        return new AppTaskOutput(
            TaskId: Guid.NewGuid().ToString(),
            AgentId: Guid.NewGuid().ToString(),
            Content: content,
            CompletedAt: DateTime.UtcNow,
            Success: true,
            ExecutionTime: TimeSpan.FromSeconds(5),
            ToolsUsed: []
        );
    }

    private static AppTaskOutput CreateTestOutputWithTools(string content, string[] tools)
    {
        var toolUsages = tools.Select(t => new Orkeon.Domain.Tools.ToolUsage(
            new Orkeon.Domain.Tools.ToolCallIdentity(Guid.NewGuid().ToString(), t, Guid.NewGuid().ToString(), Guid.NewGuid().ToString()),
            duration: TimeSpan.FromSeconds(1),
            success: true,
            error: null,
            metadata: null
        )).ToList();

        return new AppTaskOutput(
            TaskId: Guid.NewGuid().ToString(),
            AgentId: Guid.NewGuid().ToString(),
            Content: content,
            CompletedAt: DateTime.UtcNow,
            Success: true,
            ExecutionTime: TimeSpan.FromSeconds(5),
            ToolsUsed: toolUsages
        );
    }

    private static SimpleExecutionContext CreateTestContext()
    {
        var crewId = CrewId.From(Guid.NewGuid());
        var variables = new Dictionary<string, string> { { "test", "value" } }; // Add at least one variable
        using var memory = new TestMemoryScope();
        var previousOutputs = new List<AppTaskOutput>();

        return new SimpleExecutionContext(
            crewId,
            variables,
            memory,
            previousOutputs,
            CancellationToken.None
        );
    }

    private static List<DomainAgent> CreateTestAgents()
    {
        return
        [
            CreateAgent(RoleAnalyst, GoalAnalyzeData, false),
            CreateAgent(RoleDeveloper, GoalWriteCode, false),
            CreateAgent("Tester", "Test software", false)
        ];
    }

    private static List<DomainAgent> CreateTestAgentsWithTools()
    {
        var agent = new AgentBuilder()
            .Role(RoleDeveloper)
            .Goal("Write quality code")
            .Backstory("Experienced developer")
            .Build();

        // Add tools through reflection or test helper
        var toolsField = typeof(DomainAgent).GetField("_tools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (toolsField != null)
        {
            var tools = new List<Domain.Tools.IBaseTool>
            {
                new TestTool { Name = "CodeAnalyzer" },
                new TestTool { Name = "Debugger" }
            };
            toolsField.SetValue(agent, tools);
        }

        return [agent];
    }

    private static DomainAgent CreateAgent(string role, string goal, bool allowDelegation)
    {
        var builder = new AgentBuilder()
            .Role(role)
            .Goal(goal)
            .Backstory($"Backstory for {role}")
            .AllowDelegation(allowDelegation);
        var agent = builder.Build();

        return agent;
    }
}

// Test doubles

/// <summary>
/// The chat client a test hands the manager as its crew's LLM: answers the configured text (or
/// throws the configured exception) and records the prompt and the options of the last call.
/// </summary>
public sealed class TestManagerChatClient : IChatClient
{
    private string _response = "";
    private Exception? _exception;
    private string _lastPrompt = "";

    public void SetupResponse(string response)
    {
        _response = response;
        _exception = null;
    }

    public void SetupException(Exception exception)
    {
        _exception = exception;
        _response = "";
    }

    public string GetLastPrompt() => _lastPrompt;

    /// <summary>The options of the last call; null when the manager named no model.</summary>
    public ChatOptions? LastOptions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        _lastPrompt = string.Join("\n", messages.Select(m => m.Text));
        LastOptions = options;

        if (_exception != null)
        {
            throw _exception;
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _response)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The manager asks for whole answers.");

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(IChatClient) ? this : null;

    public void Dispose()
    {
        // Nothing to release.
    }
}

public class TestTool : Domain.Tools.IBaseTool
{
    public string Name { get; set; } = "TestTool";
    public string Description => "Test tool";
    public ToolSchema Schema => new ToolSchema(
        Name,
        Description,
        [],
        null
    );

    public Task<ToolCallResponse> CallAsync(
        Domain.Tools.Protocol.ToolCallRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ToolCallResponse(
            Success: true,
            Result: "Test result",
            Error: null,
            Metadata: null
        ));
    }

    public Task<Domain.Tools.ToolResult> ExecuteAsync(string input, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Domain.Tools.ToolResult.CreateSuccess("Test result"));
    }

    public bool ValidateInput(string input) => true;
}

public sealed class TestMemoryScope : IMemoryScope
{
    public string ScopeId => "test-scope";
    public string AgentId => "test-agent";

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public Task ExecuteInScopeAsync(Func<Task> action)
    {
        return action();
    }

    public Task<T> ExecuteInScopeAsync<T>(Func<Task<T>> action)
    {
        return action();
    }
}

public class TestLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _logs = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => null!;
    public bool IsEnabled(MsLogLevel logLevel) => true;

    public void Log<TState>(MsLogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _logs.Add(new LogEntry(logLevel, message));
    }

    public bool HasLoggedInformation(string containing) =>
        _logs.Any(log => log.Level == MsLogLevel.Information && log.Message.Contains(containing));

    public bool HasLoggedWarning(string containing) =>
        _logs.Any(log => log.Level == MsLogLevel.Warning && log.Message.Contains(containing));

    public bool HasLoggedError(string containing) =>
        _logs.Any(log => log.Level == MsLogLevel.Error && log.Message.Contains(containing));

    private record LogEntry(MsLogLevel Level, string Message);
}
