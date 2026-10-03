using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Constants.Protocol;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.FileSystem;
using Orkeon.Infrastructure.LLMs;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;
using CrewOutput = Orkeon.Application.Interfaces.Services.CrewOutput;

// ====================================================================
// Orkeon Streaming Demo
// Runs a crew with ICrewOrchestrationService.KickoffStreamingAsync: the same run as
// KickoffAsync — the crew's mode, its tasks and agents, its memory, knowledge and plan —
// whose events arrive as it goes: each task's start and end, each tool call, the model's
// text as it is written, and finally run.finished with the run's CrewOutput.
// ====================================================================

Console.WriteLine("=== Orkeon Streaming Demo ===\n");

// 1. The host. The model is registered the way any host registers one, with
//    AddOrkeonLlmProvider: metered, and served as the IChatClient the agents' turns stream
//    through. (A host without an IChatClient still streams every event but the text.)
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .Build();

var llmSection = config.GetSection("Llm");
var llmConfig = LlmConfig.Create(llmSection["Model"] ?? "gpt-4o-mini") with
{
    BaseUrl = llmSection["BaseUrl"] is { } baseUrl ? new Uri(baseUrl) : null,
#pragma warning disable CS0618 // demo wires the key directly from appsettings
    ApiKey = llmSection["ApiKey"],
#pragma warning restore CS0618
    Temperature = double.TryParse(llmSection["Temperature"], System.Globalization.CultureInfo.InvariantCulture, out var temperature) ? temperature : null, // absent = the model's own
    MaxTokens = int.TryParse(llmSection["MaxTokens"], out var maxTokens) ? maxTokens : null, // absent = the model's documented maximum
};

// The agents write under /output; this demo mounts a temporary folder there.
var outputDirectory = Directory.CreateTempSubdirectory("orkeon-streaming-demo-").FullName;
using var registry = new FileSystemRegistry([new FileSystemMount(outputDirectory, "/output", FileAccessRights.ReadWrite)]);

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(config);
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddHttpClient();
services.AddSingleton<IFileSystemService>(
    new FileSystemService(registry, new AllowAllPathValidator(), NullLogger<FileSystemService>.Instance));
services.AddOrkeonLlmProvider(
    sp => new OpenAIProvider(llmConfig, sp.GetRequiredService<IHttpClientFactory>(), sp.GetRequiredService<ILogger<OpenAIProvider>>()),
    llmConfig);
services.AddOrkeonApplication();
services.AddOrkeonInfrastructure();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var sp = scope.ServiceProvider;

// 2. A crew of one agent and one task, stored where the orchestrator reads them.
var analyst = new AgentBuilder()
    .Role("Research Analyst")
    .Goal("Analyze market trends and provide insights")
    .Backstory("Senior analyst with 10 years experience in tech market research")
    .AllowDelegation(false)
    .Build();

var task = new CrewTaskBuilder()
    .Description("Analyze the current state of AI agent frameworks")
    .ExpectedOutput("A concise summary of the top 3 AI agent frameworks in 2026")
    .AssignTo(analyst)
    .Build();

var crew = new CrewBuilder()
    .Name("streaming-demo")
    .Goal("Summarize the AI agent framework landscape")
    .Sequential()
    .WithAgent(analyst)
    .WithTask(task)
    .Build();

await sp.GetRequiredService<IAgentRepository>().AddAsync(analyst);
await sp.GetRequiredService<ITaskRepository>().AddAsync(task);
await sp.GetRequiredService<ICrewRepository>().AddAsync(crew);

// 3. Stream the run. Leaving the loop early (break, a cancelled token) cancels the run.
var orchestrator = sp.GetRequiredService<ICrewOrchestrationService>();
CrewOutput? output = null;

await foreach (var e in orchestrator.KickoffStreamingAsync(crew.Id, CrewInput.Empty("Focus on open-source frameworks")))
{
    switch (e.Kind)
    {
        case RunEventKinds.TaskStarted:
            Console.WriteLine($"[{e.AgentRole}] task started\n");
            break;
        case RunEventKinds.ToolCalled:
            Console.WriteLine($"\n  -> {e.ToolName}");
            break;
        case RunEventKinds.ToolReturned:
            Console.WriteLine($"  <- {e.ToolName} ({(e.Success == true ? "ok" : "failed")}, {e.Duration?.TotalMilliseconds:F0} ms)");
            break;
        case RunEventKinds.LlmDelta:
            Console.Write(e.Text); // the model's text, as it is written
            break;
        case RunEventKinds.TaskCompleted:
            Console.WriteLine($"\n\n[{e.AgentRole}] task {(e.Success == true ? "completed" : "failed")} in {e.Duration?.TotalSeconds:F1} s, {e.Tokens} tokens");
            break;
        case RunEventKinds.Error:
            await Console.Error.WriteLineAsync($"\n[{e.Code}] {e.Message}");
            break;
        case RunEventKinds.RunFinished:
            output = e.Output; // always the last event
            break;
    }
}

Console.WriteLine("\n--- Final output ---\n");
Console.WriteLine(output?.Succeeded == true ? output.FinalOutput : $"The run failed: {output?.Error}");

/// <summary>Permissive path validator for this standalone demo (mount rights still apply).</summary>
internal sealed class AllowAllPathValidator : IPathValidator
{
    public PathValidationResult ValidatePath(string requestedPath, string? workspaceRoot = null) =>
        PathValidationResult.Allowed(requestedPath);
}
