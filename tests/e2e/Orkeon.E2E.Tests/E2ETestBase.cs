using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.Task;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.SharedKernel;
using Orkeon.Infrastructure.DependencyInjection;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;
using static Orkeon.Tests.Shared.Constants.TestUrlConstants;

namespace Orkeon.E2E.Tests;

/// <summary>
/// Base class for E2E tests providing LLM provider detection, skip logic, and helper methods.
/// Tests skip gracefully when no LLM provider API key is available in the environment.
/// </summary>
public abstract class E2ETestBase : IDisposable
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly ILoggerFactory LoggerFactory;

    private static readonly string? OpenAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    private static readonly string? OllamaBaseUrl = Environment.GetEnvironmentVariable("OLLAMA_BASE_URL");

    /// <summary>
    /// Returns a skip reason if no LLM provider is available; null if tests should run.
    /// </summary>
    protected static string? LlmSkipReason =>
        HasLlmProvider ? null : "No LLM provider configured. Set OPENAI_API_KEY or OLLAMA_BASE_URL.";

    /// <summary>
    /// Skips the calling [Fact] (reported as Skipped, not Passed) when no LLM
    /// provider is configured. Call at the top of each E2E test body.
    /// </summary>
    protected static void SkipIfNoLlm() =>
        Assert.SkipWhen(LlmSkipReason is not null, LlmSkipReason ?? string.Empty);

    protected static bool HasLlmProvider =>
        !string.IsNullOrWhiteSpace(OpenAiApiKey) || !string.IsNullOrWhiteSpace(OllamaBaseUrl);

    protected static bool UseOpenAi => !string.IsNullOrWhiteSpace(OpenAiApiKey);

    protected E2ETestBase()
    {
        var services = new ServiceCollection();

        // Register an empty IConfiguration so infrastructure services (e.g. ISecretProvider) can resolve it
        var configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddLogging(b => b.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning));
        services.AddOrkeonInfrastructure();

        ServiceProvider = services.BuildServiceProvider();
        LoggerFactory = ServiceProvider.GetRequiredService<ILoggerFactory>();
    }

    /// <summary>
    /// Creates a real LLM provider based on available environment variables.
    /// Prefers OpenAI when both are configured.
    /// </summary>
    protected ILlmProvider CreateLlmProvider(string model = ModelGpt4oMini)
    {
        var factory = ServiceProvider.GetRequiredService<ILlmProviderFactory>();

        LlmConfig config;
        if (UseOpenAi)
        {
#pragma warning disable CS0618
            config = LlmConfig.Default() with
            {
                Model = model,
                ApiKey = OpenAiApiKey,
                MaxTokens = 512,
                Temperature = 0.1,
                TimeoutSeconds = 60
            };
#pragma warning restore CS0618
        }
        else
        {
            config = LlmConfig.Default() with
            {
                Model = ModelLlama2,
                BaseUrl = new Uri(OllamaBaseUrl ?? EndpointOllamaDefault),
                MaxTokens = 512,
                Temperature = 0.1,
                TimeoutSeconds = 60
            };
        }

        var provider = factory.Create(config);
        if (provider is not ILlmProvider llmProvider)
            throw new InvalidOperationException("Factory returned a provider that is not ILlmProvider.");
        return llmProvider;
    }

    /// <summary>
    /// Creates a basic Agent for testing.
    /// </summary>
    protected static Domain.Agent.Agent CreateTestAgent(
        string role = "Test Agent",
        string goal = "Complete test tasks accurately",
        string? backstory = null)
    {
        return new AgentBuilder()
            .Role(role)
            .Goal(goal)
            .Backstory(backstory ?? $"A helpful {role.ToLower()} for testing.")
            .MaxIterations(3)
            .MaxRpm(10)
            .MaxRetryLimit(1)
            .Build();
    }

    /// <summary>
    /// Creates a basic Task for testing.
    /// </summary>
    protected static Domain.Task.CrewTask CreateTestTask(
        string description = "Complete the assigned test task.",
        string expectedOutput = "A concise test result.")
    {
        return new CrewTaskBuilder()
            .Description(description)
            .ExpectedOutput(expectedOutput)
            .Build();
    }

    /// <summary>
    /// Creates a basic Crew for testing.
    /// </summary>
    protected static Domain.Crew.Crew CreateTestCrew(string goal = "Complete test objectives")
    {
        return new CrewBuilder()
            .Goal(goal)
            .Sequential()
            .MaxRpm(10)
            .Build();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            LoggerFactory.Dispose();
            if (ServiceProvider is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
