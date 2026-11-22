# Orkeon.Interop.AgentCli

Part of [Orkeon](https://github.com/Orkeon/orkeon) — AI agent teams that stay inside the lines.

**Experimental** (`ORKEXP008`, [ADR-020](https://github.com/Orkeon/orkeon/blob/main/docs/adr/ADR-020-agent-cli-provider.md)):
an LLM provider, `Llm:Provider = agent-cli`, answered by an agent CLI on its own login rather than an API
key. Its one engine is Claude Code: `claude -p` in stream-json, signed in with `claude /login`.

- **Orkeon keeps the conversation and runs every tool.** The CLI is started with none of its own tools;
  Orkeon's tools reach it through an MCP server the provider serves over a local socket, and each call
  ends on the assistant message, so Orkeon's loop runs the tool — its gates, approvals and modes apply —
  and hands the result back.
- **One process per conversation, for a run.** A call that extends what the CLI already holds writes only
  what is new; any other call starts a new process, seeded with the history.
- **Personal use, on the user's subscription.** The CLI's API key variables are removed from its
  environment, and a CLI that reports anything but its login is refused (`Llm:AgentCli:RequireLogin`).
  The cost reported is 0; the CLI's notional figure is kept under `agent_cli_notional_cost_usd`.

```json
{
  "Llm": {
    "Provider": "agent-cli",
    "Model": "haiku",
    "AgentCli": { "Command": "claude", "MaxProcesses": 4 }
  }
}
```

```csharp
services.AddOrkeonAgentCli();   // the orkeon CLI, the REPL and orkeon-host already do
```

Options other than the model (temperature, max tokens, thinking, ...) are not sent: each one declared is
reported once per run as a warning.

Depends on the `Orkeon` umbrella package and on the `claude` CLI, installed and signed in.
