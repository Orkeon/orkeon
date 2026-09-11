# Microsoft Agent Framework interop

The two directions of `Orkeon.Interop.AgentFramework`, on one host and one model:

1. **Orkeon → MAF** — an Orkeon crew (one summariser agent) is wrapped as a MAF `AIAgent`
   (`CrewAgent`, through `ICrewAgentFactory`) and called with `AIAgent.RunAsync(...)`, the
   way any MAF workflow would call it.
2. **MAF → Orkeon** — a MAF `ChatClientAgent` is built over Orkeon's own configured model
   (`LlmProviderToChatClientAdapter` + `AsAIAgent()`), then handed to an Orkeon agent as a
   tool (`WithAgentFrameworkTool`). The Orkeon agent writes a plan and delegates the review.

## Run

```bash
# the default settings chain (examples/appsettings/appsettings.json -> Docker Model Runner)
dotnet run --project examples/interop/agent-framework/AgentFrameworkInterop.csproj

# any other model, e.g. the Ollama profile of the README quickstart
dotnet run --project examples/interop/agent-framework/AgentFrameworkInterop.csproj -- --settings examples/quickstart/appsettings.json
```

Without a configured model the echo provider answers and the exchange still shows the two
call shapes. `AgentFrameworkInterop.csproj` references `Microsoft.Agents.AI` (not only the
abstractions) for `AsAIAgent()`; the interop library itself depends on the abstractions only.
