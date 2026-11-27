# Orkeon.Interop.AgentCli

Part of [Orkeon](https://github.com/Orkeon/orkeon) — AI agent teams that stay inside the lines.

**Experimental** (`ORKEXP010`, [ADR-020](https://github.com/Orkeon/orkeon/blob/main/docs/adr/ADR-020-agent-cli-provider.md)):
an LLM provider, `Llm:Provider = agent-cli`, answered by an agent CLI on its own login rather than an API
key. Its one engine is Claude Code: `claude -p` in stream-json, signed in with `claude /login`.

- **Orkeon keeps the conversation and runs every tool.** The CLI is started with none of its own tools;
  Orkeon's tools reach it through an MCP server the provider serves over a local socket, and each call
  ends on the assistant message, so Orkeon's loop runs the tool — its gates, approvals and modes apply —
  and hands the result back.
- **One process per conversation, for a run.** A call that extends what the CLI already holds writes only
  what is new; any other call starts a new process, seeded with the history.
- **Personal use, on the user's subscription.** Every `ANTHROPIC_*` variable of the host (the API key,
  `ANTHROPIC_BASE_URL`, `ANTHROPIC_CUSTOM_HEADERS`, ...) and the cloud switches are removed from its
  environment, so that nothing set for Orkeon's own `anthropic` provider steers the CLI or its login; the
  rest stays (`PATH`, proxies, certificate bundles, `CLAUDE_CONFIG_DIR`). A CLI that reports anything but
  its login is refused (`Llm:AgentCli:RequireLogin`).
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

A thinking effort (`low` to `max`) starts the CLI with `--effort`; a call at another level gets a process
of its own. Options other than the model and the effort (temperature, max tokens, a thinking budget or
switch, `Llm:WorkspaceId`, a `Required` tool mode, ...) are not sent: each one declared is reported once
per run as a warning. A call runs under
`Llm:TimeoutSeconds`, and `Llm:StreamIdleSeconds` when set: past either, the turn is interrupted, its
process closed, and the call fails as an HTTP provider's does. On Windows, `Command` names an executable
(`claude.exe`): a `.cmd` or `.bat` script, which would run through `cmd.exe`, is refused at start.

Depends on the `Orkeon` umbrella package and on the `claude` CLI, installed and signed in.
