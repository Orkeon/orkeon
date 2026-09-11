# Orkeon.Interop.AgentFramework

Part of [Orkeon](https://github.com/Orkeon/orkeon) — AI agent teams that stay inside the lines.

The bridge to **Microsoft Agent Framework** (`Microsoft.Agents.AI`), in both directions:

- **An Orkeon crew as a MAF `AIAgent`** — `CrewAgent`. Every `RunAsync` is one crew
  kickoff: the conversation becomes the crew's initial context, the crew's final output
  comes back as the assistant message, token telemetry as `Usage`. Drop it into any MAF
  workflow, orchestration or `AsAIFunction()` chain.
- **A MAF `AIAgent` inside an Orkeon crew**, two ways:
  - as the **brain** of an Orkeon agent — `new AgentBuilder().WithAgentFrameworkAgent(agent)`:
    the Orkeon agent keeps its role, goal and tasks; what answers is the MAF agent, on one
    continuous MAF session (`AIAgentLlmProvider`);
  - as a **tool** — `WithAgentFrameworkTool(agent)` (`AIAgentTool`): the Orkeon agent
    delegates to the MAF agent like to any other tool, the mirror of MAF's `AsAIFunction()`.

```csharp
// Orkeon -> MAF
services.AddOrkeonAgentFramework();
var crewAgent = host.Services.GetRequiredService<ICrewAgentFactory>().Create(crew);
var answer = await crewAgent.RunAsync("Summarise last week's incidents");

// MAF -> Orkeon
AIAgent reviewer = chatClient.AsAIAgent(name: "Reviewer", instructions: "Review code for security issues.");
var orkeonAgent = new AgentBuilder().Role("Auditor").Goal("Audit the change")
    .WithAgentFrameworkTool(reviewer)      // delegate to it...
    .WithAgentFrameworkAgent(reviewer)     // ...or let it answer for the Orkeon agent
    .Build();
```

Depends on the `Orkeon` umbrella package and on `Microsoft.Agents.AI.Abstractions`.
The runnable example lives in `examples/interop/agent-framework/`.

MIT © Orkeon Contributors
