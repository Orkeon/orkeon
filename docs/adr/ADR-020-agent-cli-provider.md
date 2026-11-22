> 🇫🇷 [Version française](../fr/adr/ADR-020-agent-cli-provider.md)

> **See also**: [ADR-010](./ADR-010-agent-framework-interop.md) · [ADR-019](./ADR-019-tool-bridge.md) · [Back to the index](../INDEX.md)

# ADR-020 — An experimental `agent-cli` provider: an agent CLI answers, Orkeon keeps its tools and its history

**Status**: Accepted (experimental, `ORKEXP010`) · **Date**: 2026-11-22
· **Scope**: `Orkeon.Application` (`Interfaces/Ports/ILlmProviderModule.cs`), `Orkeon.Infrastructure` (`LLMs/LlmProviderFactory.cs`, `LLMs/Profiles/LlmSettings.cs`), `Orkeon.Interop.AgentCli`, `Orkeon.Interop.AgentCli.Relay`, and the three binaries that register it (`orkeon`, `orkeon-repl`, `orkeon-host`).

## Context

A user who holds a Claude subscription and no API key cannot run Orkeon on it: every provider speaks a vendor's
HTTP API and bills a key. The vendor's own agent CLI, `claude`, runs on the subscription it is signed in to, and in
print mode (`claude -p --input-format stream-json --output-format stream-json`) it reads turns on its standard input
and writes every event of a turn on its output. Driving that process gives Orkeon a model on the subscription, without
reimplementing the vendor's sign-in. The CLI is an agent, not a model endpoint: it has its own tools, keeps its own
conversation and compacts it when it likes. Orkeon's contract is the opposite: a crew sends the whole history, the
tools are Orkeon's and its pipeline judges every call. The scope of the framework is frozen at its sixteen providers;
a new one needs a motivated exception.

## Decision

1. **A provider shipped outside the core.** `Llm:Provider` may name any key. `mock` keeps its meaning; any other key
   is a **provider module** (`ILlmProviderModule`, registered in the container): the factory hands it the section
   and builds the provider through the same entrance as the others (cassette, call mode, metering), never infers
   another provider from the address or the model's name, and refuses a key no module answers. `orkeon doctor` and
   `orkeon llm probe` read the modules the tool ships.
2. **The key is generic.** The module's key is `agent-cli` and its engine `claude` (`Llm:AgentCli:Engine`): no
   vendor's name in the provider's key, so that another CLI is a second engine, not a second provider.
   Another engine is refused when the host starts.
3. **Orkeon's tools, run by Orkeon.** The CLI starts with its own tools turned off (`--tools ""`, `--strict-mcp-config`)
   and one MCP server, `orkeon`, reached through a relay the CLI starts (`Orkeon.Interop.AgentCli.Relay`, over a Unix
   domain socket in a private scratch folder); the bridge speaks JSON-RPC through Orkeon's own `JsonRpcTypes`, a
   stable envelope. When the model calls a tool, the provider stops the turn on the `message_stop` that ends it and
   answers the crew with Anthropic `tool_use` blocks; the crew runs the tool through its own pipeline, and the next
   call hands the result to the call the CLI is waiting on. A denied call reaches the CLI as an error result.
4. **Orkeon keeps the history.** Each call sends the whole history, as with any provider. The provider keeps one CLI
   process per conversation for the run (`AgentRunScope`), and continues it when the history extends what the CLI
   already holds; any other history — rewritten, compacted by Orkeon, or a CLI that compacted itself — starts a new
   process seeded with the history as text. The CLI's own compaction is pushed beyond every window
   (`Llm:AgentCli:AutocompactTokens`).
5. **On the login, never a key.** Every `ANTHROPIC_*` variable of the host (`ANTHROPIC_API_KEY` and
   `ANTHROPIC_AUTH_TOKEN`, which would put the CLI on a key, but also `ANTHROPIC_BASE_URL`, which would send the CLI
   and its login to a proxy set for Orkeon's own `anthropic` provider, `ANTHROPIC_CUSTOM_HEADERS`, `ANTHROPIC_MODEL`
   and the others) and the switches to a cloud vendor's account (`CLAUDE_CODE_USE_BEDROCK`, `CLAUDE_CODE_USE_VERTEX`,
   `CLAUDE_CODE_USE_FOUNDRY`) are removed from the CLI's environment, by name and case-insensitively on Windows; the
   rest stays (`PATH`, the home folder, the proxies, the certificate bundles, `CLAUDE_CONFIG_DIR` where the login
   lives). An answer on a key rather than the login is refused (`Llm:AgentCli:RequireLogin`). The cost is 0: the vendor's
   figure is kept as `agent_cli_notional_cost_usd`. A subscription limit (`rate_limit_event` rejected) is a rate
   limit, so `Fallback` takes over.
6. **Processes are bounded and never outlive the host.** `Llm:AgentCli:MaxProcesses` (4) closes an idle session
   first and never waits; a cancelled call interrupts the turn and closes its process, and so does a call that
   outlives `Llm:TimeoutSeconds` or stays silent for `Llm:StreamIdleSeconds`, which fails with the error type the HTTP
   providers give it (`TaskCanceledException`, `StreamIdleTimeout`); the relay kills the CLI's process tree when the
   host's socket closes, even after a `kill -9` of the host. `Llm:AgentCli` is judged when the host starts: an empty
   `Command` or a `.cmd`/`.bat` one (which Windows runs through `cmd.exe`), a `MaxProcesses` or `AutocompactTokens`
   under 1 and a `MaxToolResultCharacters` under 1000 are refused, naming the key.
7. **Experimental.** Every public type and member of `Orkeon.Interop.AgentCli` carries `[Experimental("ORKEXP010")]`
   — but `AgentCliDiagnostics`, which holds that identifier, and the class that holds `AddOrkeonAgentCli`, itself
   experimental. The seam of decision 1 (`ILlmProviderModule`, `LlmProviderModuleContext`,
   `LlmProviderFactoryOptions.Modules`) is stable. The project lives in `src/interop`, and the provider is not
   counted among the sixteen. It is meant for personal use, on the subscription of whoever
   runs it.

## Consequences

- Orkeon runs on a Claude subscription with the crew, the tools and the pipeline it has on an API key.
- A call costs a CLI process start on a new conversation, and the vendor's system prompt and tool rules surround
  Orkeon's; the answers may differ from the same model behind the API.
- Options the CLI cannot receive (temperature, max tokens, top-p, the penalties, a seed, stop sequences, a response
  format, a thinking budget, a grammar, cache breakpoints, `Llm:WorkspaceId`, a `Required` tool mode) are each warned
  about once per run and not sent. A `None` tool mode offers the CLI no tool.
- The CLI's stream is a vendor's protocol that may change with its releases; the experimental status covers it.
- Left for later: the CLI's own tools behind Orkeon's gate, a provider that keeps the context itself, more engines.
