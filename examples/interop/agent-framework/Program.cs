using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
//                      becomes a tool of an Orkeon agent, which delegates to it;
//   3. MAF -> Orkeon   the same MAF agent answers for another Orkeon agent: it is that agent's
//                      model (WithAgentFrameworkAgent), and every turn of its task is a MAF run.
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

        // ── 1. Orkeon -> MAF ────────────────────────────────────────────────────────────
        // The crew agent runs every turn in a scope of its own: the loader registers the crew
        // in that scope's repositories (they are scoped), the scope's orchestrator runs it.
        AIAgent crewAsMafAgent = host.Services.GetRequiredService<ICrewAgentFactory>().Create(
            async (services, ct) =>
            {
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
                await services.GetRequiredService<IAgentRepository>().AddAsync(summariser, ct);
                await services.GetRequiredService<ITaskRepository>().AddAsync(summarise, ct);
                await services.GetRequiredService<ICrewRepository>().AddAsync(summaryCrew, ct);
                return summaryCrew.Id;
            },
            "orkeon-summariser",
            "Summarises text in two sentences");

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
        // A MAF agent over Orkeon's configured model, given to an Orkeon agent as a tool. The
        // crew is registered and run in one scope: the orchestrator and its repositories are
        // scoped services.
        var provider = host.Services.GetRequiredService<ILlmProvider>();
        await using var scope = host.Services.CreateAsyncScope();
        var agents = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        var tasks = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        var crews = scope.ServiceProvider.GetRequiredService<ICrewRepository>();
        var orchestrator = scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>();
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
        Console.WriteLine();

        // ── 3. MAF -> Orkeon, as the model ──────────────────────────────────────────────
        // The same MAF reviewer answers for another Orkeon agent: the auditor keeps its role, goal
        // and task, and each of its turns is a run of the reviewer, which receives the prompt Orkeon
        // composed. The run meters it as the auditor's work, once: the reviewer is built over the
        // configured model, metered already, so that model's meter counts and the bridge's stays
        // silent. A MAF agent calls its own tools, never Orkeon's: the auditor carries none (the build
        // refuses one), and an agent that needs both holds the MAF agent as a tool, as in section 2.
        var auditor = new AgentBuilder()
            .Role("Auditor")
            .Goal("Name the biggest risk of a plan, in one sentence")
            .WithAgentFrameworkAgent(reviewer, host.Services.GetRequiredService<ILogger<AIAgentLlmProvider>>())
            .Build();
        var audit = new CrewTaskBuilder()
            .Description("Name the biggest risk of this plan: add a CI job that runs the tests on every push, " +
                "caches the NuGet packages between runs and publishes the coverage report.")
            .ExpectedOutput("The biggest risk, in one sentence.")
            .AssignTo(auditor)
            .Build();
        var auditCrew = new CrewBuilder().Goal("Audit a plan").Sequential()
            .WithAgent(auditor).WithTask(audit).Build();
        await agents.AddAsync(auditor);
        await tasks.AddAsync(audit);
        await crews.AddAsync(auditCrew);

        Console.WriteLine("── 3. A MAF agent answering for an Orkeon agent ──");
        var answered = await orchestrator.KickoffAsync(auditCrew.Id, Orkeon.Application.Interfaces.Services.CrewInput.Empty("Audit the CI plan"));
        Console.WriteLine(answered.FinalOutput);
        Console.WriteLine($"   [succeeded: {answered.Succeeded}, {answered.Duration.TotalSeconds:F1}s]");
        return orkeonSide.Succeeded && answered.Succeeded ? 0 : 1;
    }
}
