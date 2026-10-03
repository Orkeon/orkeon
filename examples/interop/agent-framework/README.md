# Microsoft Agent Framework interop

The two directions of `Orkeon.Interop.AgentFramework`, on one host and one model:

1. **Orkeon → MAF** — an Orkeon crew (one summariser agent) is wrapped as a MAF `AIAgent`
   (`CrewAgent`, through `ICrewAgentFactory`) and called with `AIAgent.RunAsync(...)`, the
   way any MAF workflow would call it.
2. **MAF → Orkeon, as a tool** — a MAF `ChatClientAgent` is built over Orkeon's own configured
   model (`LlmProviderToChatClientAdapter` + `AsAIAgent()`), then handed to an Orkeon agent as a
   tool (`WithAgentFrameworkTool`). The Orkeon agent writes a plan and delegates the review.
3. **MAF → Orkeon, as the model** — the same MAF agent answers for another Orkeon agent, the
   auditor (`WithAgentFrameworkAgent`): every turn of the auditor's task is a run of the MAF agent,
   which receives the prompt Orkeon composed — role, goal, task, expected output. The run meters it
   once, as the auditor's work: the MAF agent is built over the configured model, metered already, so
   that model's meter counts. The auditor carries no Orkeon tool: a MAF agent calls its own, and the
   build refuses one. An agent that needs both holds the MAF agent as a tool, as in section 2.

## Run

```bash
# the default settings chain (examples/appsettings/appsettings.json -> Docker Model Runner)
dotnet run --project examples/interop/agent-framework/AgentFrameworkInterop.csproj

# any other model, e.g. the Ollama profile of the README quickstart
dotnet run --project examples/interop/agent-framework/AgentFrameworkInterop.csproj -- --settings examples/quickstart/appsettings.json
```

Without a configured model (a settings file with no `Llm` section) the echo provider answers and
the exchange still shows the three call shapes. `AgentFrameworkInterop.csproj` references
`Microsoft.Agents.AI` (not only the abstractions) for `AsAIAgent()`; the interop library itself
depends on the abstractions only.
