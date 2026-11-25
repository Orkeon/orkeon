> 🇫🇷 [Version française](../fr/adr/ADR-023-chat-attach-tool.md)

> **See also**: [ADR-021](./ADR-021-chat-tool-family.md) · [Service host](../architecture/service-host.md) · [Back to the index](../INDEX.md)

# ADR-023 — `chat_attach`: an agent sends files to the conversation of its run

**Status**: Accepted · **Date**: 2026-11-25
· **Scope**: one built-in tool in `Orkeon.Hosting`, its conversation in `Orkeon.Host`

## Context

A hosted run leaves files in `/outbox`; a person takes them with `/get`. An agent cannot hand one over in the course of its work. The conversation belongs to the host: an `.ork.ts` script or a plugin does not hold it, so the freeze's usual answer does not apply. The scope is frozen (CONTRIBUTING, "The scope is frozen").

## Decision

1. **One tool, `chat_attach`, the sixth motivated exception**, decided by the owner on 2026-11-25. It takes paths and no address.
2. **The conversation is ambient.** `CrewRunner` enters a scope that flows with the run, read at each call. Everything the run starts sees it; no other run does. It closes with the run. Outside one, the call is a plain tool error.
3. **One implementation of what may leave**, shared with `/get`: `/outbox` and the folders the crew declares `download`, held to the virtual file system; a forbidden file is an absent one. The declaration is read as the host; the file is read under the scope of the agent that asked, when the channel opens it too.
4. **A run is bounded**: files, messages and bytes, counted before sending, all its agents together.
5. **It is not of the chat family**: it uses no account and needs no right of ADR-021.

### Decisions retained

| Question | Retained |
|---|---|
| Where it is registered | Beside `human_input`: the run of a YAML crew, its listing, `orkeon doctor`, `orkeon-host`, the REPL. Not in `RunnerHost`, so `orkeon mcp serve` does not expose it and the forge does not draw it. A crew that names it loads everywhere. |
| Its access | Execute: what is sent cannot be taken back. `StaysWithCaller`: it never crosses a tool bridge. |
| `/inbox` | Never sent: the person has those files, and one taken back from the channel must not come out again. |
| Teams | Each file is refused until the channel sends files. |
| What counts | All but what the channel found too large, which was never posted. |

## Alternatives considered

- **Sending `/outbox` at the end of each run**: unasked files in a thread, and nothing in the course of the work.
- **A `conversation` argument**: a model would choose who reads the file.
- **A host that registers the tool only in a conversation**: a crew would load in one host and fail in another.

## Consequences

- 135 built-in tools.
- A file sent is seen by everyone in the thread, where `/get` answers one person.
