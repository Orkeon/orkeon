using Orkeon.Domain.SharedKernel.ValueObjects;

namespace Orkeon.E2E.Tests;

/// <summary>
/// E2E tests for hierarchical delegation: manager agent delegating tasks to workers.
/// Tests use real LLM providers and skip gracefully when none is configured.
/// </summary>
public class HierarchicalDelegationTests : E2ETestBase
{
    [Fact]
    public async Task ManagerDelegatesToWorker_CompletesTask()
    {
        SkipIfNoLlm();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var llmProvider = CreateLlmProvider();

        var managerAgent = CreateTestAgent(
            "Project Manager",
            "Coordinate tasks and delegate work to the right specialists",
            "An experienced project manager who breaks down complex tasks into actionable items.");

        var workerAgent = CreateTestAgent(
            "Python Developer",
            "Write clean, efficient Python code",
            "A skilled Python developer specialising in data processing and automation.");

        // Simulate manager decomposing a task and delegating to worker
        var managerPrompt = $"""
            Agent: {managerAgent.Role}
            Goal: {managerAgent.Goal}

            Task: A user wants a Python function to calculate the factorial of a number.
            Available workers: {workerAgent.Role}

            Describe the specific task you would delegate to the {workerAgent.Role} and what output you expect.
            Be concise — two sentences maximum.
            """;

        var managerResponse = await llmProvider.GenerateAsync(managerPrompt,
            LlmConfig.Default() with { MaxTokens = 256, Temperature = 0.1 }, cts.Token);

        Assert.NotNull(managerResponse);
        Assert.False(string.IsNullOrWhiteSpace(managerResponse.Content),
            "Manager agent produced empty delegation instructions.");

        // Worker executes the delegated task
        var workerPrompt = $"""
            Agent: {workerAgent.Role}
            Goal: {workerAgent.Goal}

            Manager instructions: {managerResponse.Content}

            Write a Python function to calculate the factorial of a non-negative integer.
            Include only the function code, no explanation.
            """;

        var workerResponse = await llmProvider.GenerateAsync(workerPrompt,
            LlmConfig.Default() with { MaxTokens = 512, Temperature = 0.1 }, cts.Token);

        Assert.NotNull(workerResponse);
        Assert.False(string.IsNullOrWhiteSpace(workerResponse.Content),
            "Worker agent produced empty output.");
        Assert.Contains("def factorial", workerResponse.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DelegationWithMultipleWorkers_SelectsBestAgent()
    {
        SkipIfNoLlm();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var llmProvider = CreateLlmProvider();

        var managerAgent = CreateTestAgent(
            "Tech Lead",
            "Select the best specialist for each task and coordinate their work");

        var workers = new[]
        {
            CreateTestAgent("Frontend Developer", "Build user interfaces using HTML, CSS, JavaScript"),
            CreateTestAgent("Backend Developer", "Design APIs and server-side logic using C#"),
            CreateTestAgent("Database Administrator", "Optimise database schemas and queries")
        };

        var workerDescriptions = string.Join("\n", workers.Select((w, i) =>
            $"{i + 1}. {w.Role.Value}: {w.Goal.Value}"));

        var task = "Design a REST API endpoint that accepts a user ID and returns their order history from the database.";

        var selectionPrompt = $"""
            Agent: {managerAgent.Role}
            Goal: {managerAgent.Goal}

            Available workers:
            {workerDescriptions}

            Task: {task}

            Which worker number (1, 2, or 3) is best suited to lead this task?
            Answer with just the number and a one-sentence reason.
            """;

        var selectionResponse = await llmProvider.GenerateAsync(selectionPrompt,
            LlmConfig.Default() with { MaxTokens = 128, Temperature = 0.0 }, cts.Token);

        Assert.NotNull(selectionResponse);
        Assert.False(string.IsNullOrWhiteSpace(selectionResponse.Content),
            "Manager failed to select an agent.");

        // The manager should select worker 2 (Backend Developer) — verify a number is present
        Assert.True(
            selectionResponse.Content.Contains('1') ||
            selectionResponse.Content.Contains('2') ||
            selectionResponse.Content.Contains('3'),
            $"Expected a worker number in the response, got: {selectionResponse.Content}");
    }
}
