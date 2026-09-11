using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Task;
using Orkeon.Hosting;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Interop.AgentFramework;
using Orkeon.Interop.AgentFramework.DependencyInjection;

namespace Orkeon.Examples.AgentFrameworkInterop;

// The two directions of the Microsoft Agent Framework bridge, on one host and one model:
//
//   1. Orkeon -> MAF   an Orkeon crew runs as a MAF AIAgent (CrewAgent) -- any MAF workflow,
//                      orchestration or AsAIFunction() chain can call it;
//   2. MAF -> Orkeon   a MAF ChatClientAgent (built over Orkeon's own configured model)
//                      becomes a tool of an Orkeon agent, which delegates to it.
//
// The model is whatever the settings chain resolves (examples/appsettings/appsettings.json
// by default -- Docker Model Runner -- or --settings <file>, or `orkeon init`); without one
// the echo provider answers and the shape of the run is still visible.
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var settings = args.Length >= 2 && args[0] == "--settings" ? args[1] : null;
        var output = Directory.CreateTempSubdirectory("orkeon-maf-").FullName;

        using var host = RunnerHost.Build(
            settings,
            new RunnerMountPlan { CliMounts = [$"{output}:/output:rw"], AllowExternalMounts = true },
            configureServices: (_, services) => services.AddOrkeonAgentFramework());

        var orchestrator = host.Services.GetRequiredService<ICrewOrchestrationService>();
        var provider = host.Services.GetRequiredService<ILlmProvider>();
        var agents = host.Services.GetRequiredService<IAgentRepository>();
        var tasks = host.Services.GetRequiredService<ITaskRepository>();
        var crews = host.Services.GetRequiredService<ICrewRepository>();

        // ── 1. Orkeon -> MAF ────────────────────────────────────────────────────────────
        var summariser = new AgentBuilder()
            .Role("Summariser")
            .Goal("Summarise any text in exactly two sentences")
            .Build();
        var summarise = new CrewTaskBuilder()
            .Description("Summarise the text given as initial context in exactly two sentences.")
            .ExpectedOutput("Two sentences.")
            .AssignTo(summariser)
            .Build();
        var summaryCrew = new CrewBuilder().Goal("Summarise text").Sequential()
            .WithAgent(summariser).WithTask(summarise).Build();
        await agents.AddAsync(summariser);
        await tasks.AddAsync(summarise);
        await crews.AddAsync(summaryCrew);

        AIAgent crewAsMafAgent = host.Services.GetRequiredService<ICrewAgentFactory>()
            .Create(summaryCrew.Id, "orkeon-summariser", "Summarises text in two sentences");

        Console.WriteLine("── 1. An Orkeon crew called from MAF (AIAgent.RunAsync) ──");
        var mafSide = await crewAsMafAgent.RunAsync(
            "Orkeon puts a boundary in front of the model: agents only see virtual paths, " +
            "each one a declared mount with declared rights, and a path outside a mount is " +
            "refused before any byte lands on disk. Around that boundary sits a complete " +
            "agent-team framework with six orchestration strategies and fourteen LLM providers.");
        Console.WriteLine(mafSide.Text);
        Console.WriteLine($"   [agent {mafSide.AgentId}, {mafSide.Usage?.TotalTokenCount.ToString() ?? "unmetered"} tokens]");
        Console.WriteLine();

        // ── 2. MAF -> Orkeon ────────────────────────────────────────────────────────────
        // A MAF agent over Orkeon's configured model, given to an Orkeon agent as a tool.
        using IChatClient chatClient = new LlmProviderToChatClientAdapter(provider);
        AIAgent reviewer = chatClient.AsAIAgent(
            name: "Reviewer",
            instructions: "You review short plans and answer with the single biggest risk, in one sentence.",
            description: "Names the biggest risk of a plan");

        var planner = new AgentBuilder()
            .Role("Planner")
            .Goal("Write a three-step plan and have it reviewed")
            .WithAgentFrameworkTool(reviewer, "ask_reviewer", "Ask the reviewer for the biggest risk of a plan")
            .MaxIterations(4)
            .Build();
        var plan = new CrewTaskBuilder()
            .Description("Write a three-step plan to add a CI job to a repository, then call ask_reviewer with the plan and append its answer under a 'Risk' heading.")
            .ExpectedOutput("The plan and the reviewer's risk.")
            .AssignTo(planner)
            .Build();
        var planCrew = new CrewBuilder().Goal("Plan and review").Sequential()
            .WithAgent(planner).WithTask(plan).Build();
        await agents.AddAsync(planner);
        await tasks.AddAsync(plan);
        await crews.AddAsync(planCrew);

        Console.WriteLine("── 2. A MAF agent used as a tool by an Orkeon agent ──");
        var orkeonSide = await orchestrator.KickoffAsync(planCrew.Id, Orkeon.Application.Interfaces.Services.CrewInput.Empty("Add a CI job"));
        Console.WriteLine(orkeonSide.FinalOutput);
        Console.WriteLine($"   [succeeded: {orkeonSide.Succeeded}, {orkeonSide.Duration.TotalSeconds:F1}s]");
        return orkeonSide.Succeeded ? 0 : 1;
    }
}
