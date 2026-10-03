# Orkeon.Interop.AgentFramework

Part of [Orkeon](https://github.com/Orkeon/orkeon) — AI agent teams that stay inside the lines.

The bridge to **Microsoft Agent Framework** (`Microsoft.Agents.AI`), in both directions:

- **An Orkeon crew as a MAF `AIAgent`** — `CrewAgent`. Every `RunAsync` is one crew
  kickoff: the conversation becomes the crew's initial context, the crew's final output
  comes back as the assistant message, token telemetry as `Usage`. Drop it into any MAF
  workflow, orchestration or `AsAIFunction()` chain.
- **A MAF `AIAgent` inside an Orkeon crew**, two ways:
  - as the **brain** of an Orkeon agent — `new AgentBuilder().WithAgentFrameworkAgent(agent)`:
    the Orkeon agent keeps its role, goal and tasks; what answers is the MAF agent — every turn of
    its tasks, metered once as its work —, on one continuous MAF session that hears each message
    once (`AIAgentLlmProvider`). A MAF agent calls its own tools, never Orkeon's: such an agent
    carries no Orkeon tool, and the build refuses one;
  - as a **tool** — `WithAgentFrameworkTool(agent)` (`AIAgentTool`): the Orkeon agent
    delegates to the MAF agent like to any other tool, the mirror of MAF's `AsAIFunction()`.

```csharp
// Orkeon -> MAF
services.AddOrkeonAgentFramework();
// every turn runs in a scope of its own: register the crew there and return its id
var crewAgent = host.Services.GetRequiredService<ICrewAgentFactory>().Create(
    async (services, ct) => (await services.GetRequiredService<ICrewFactory>()
        .CreateFromFileAsync("/crew/crew.yaml", ct)).Id,
    "summariser");
var answer = await crewAgent.RunAsync("Summarise last week's incidents");

// MAF -> Orkeon: two agents — one delegates to the MAF agent, the other is answered by it
AIAgent reviewer = chatClient.AsAIAgent(name: "Reviewer", instructions: "Review code for security issues.");
var planner = new AgentBuilder().Role("Planner").Goal("Plan the change and have it reviewed")
    .WithAgentFrameworkTool(reviewer)      // a tool among its tools
    .Build();
var auditor = new AgentBuilder().Role("Auditor").Goal("Audit the change")
    .WithAgentFrameworkAgent(reviewer)     // its model: no Orkeon tool beside it
    .Build();
```

Depends on the `Orkeon` umbrella package and on `Microsoft.Agents.AI.Abstractions`.
The runnable example lives in `examples/interop/agent-framework/`.

MIT © Orkeon Contributors
