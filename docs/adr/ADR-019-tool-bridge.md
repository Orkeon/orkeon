> 🇫🇷 [Version française](../fr/adr/ADR-019-tool-bridge.md)

> **See also**: [ADR-017](./ADR-017-transcript-display-model.md) · [ADR-018](./ADR-018-graph-engines-shared-topology-and-yaml-graph-block.md) · [Back to the index](../INDEX.md)

# ADR-019 — A tool bridge: some tools of a run execute on another machine, over a protocol of its own

**Status**: Accepted · **Date**: 2026-11-21
· **Scope**: `Orkeon.Domain` (`Attributes/ToolContractAttribute.cs`, `Tools/ToolContracts.cs`), `Orkeon.Hosting` (`ToolBridge/`), `Orkeon.Host` (`ToolBridgeConnectionService.cs`), `Orkeon.Scripting` (`ScriptToolCatalogue.cs`). Issue #445.

## Context

An Orkeon that runs in a container has no access to what lives on the user's machine: the browser where the user is
signed in, a desktop chat client, a local device. The tools that drive those things must run where they are, while the
run, its model calls, its pipeline and its workspace stay in the container. MCP could carry the calls, but an MCP
server is a tool provider the caller does not know in advance: its tools arrive with their own schema and no access
class, and the caller's pipeline cannot judge them as it judges a built-in tool. The tools to move are built-in
tools, which both sides know.

## Decision

1. **A protocol of its own, JSON over HTTP, not MCP.** Version 1 sits under `/orkeon-bridge/v1/`: `GET tools` (the
   tools served, each with a contract hash), `POST call` (one call: an id, the tool, its arguments, its context; the
   tool's raw response back), `POST cancel` (the cancellation of a call in flight), `POST end-run` (the end of a
   caller's run) and `POST heartbeat` (a caller with live runs is still there). Every request carries the token as
   `Authorization: Bearer`. A later version adds fields and endpoints beside these; a field a side does not know is
   ignored.
2. **Both sides select.** The caller's section `Orkeon:Tools:Bridge` (`Address`, `TokenEnvVar`, `Allow`, `Deny`) and the
   server's `Orkeon:Bridge` (`Prefix`, `TokenEnvVar`, `Allow`, `Deny`, `MaxRequestBytes`) apply one rule: no `Allow`
   selects nothing, the lone `*` selects every tool, `Deny` wins, only names and the lone `*` are accepted, and a name
   that is no tool of the side's registry refuses the start. A tool is bridged when both sides select it. A coworker
   delegation tool is never selected: it stands for the caller's own crew.
3. **The workspace stays home.** A tool that acts on the workspace (files, shell, code index) declares
   `[ToolContract(..., ActsOnWorkspace = true)]`, read through wrappers by `ToolContracts.ActsOnWorkspace`. `*` never
   selects such a tool on either side; naming one bridges it, and each side logs a warning naming it.
4. **No start without the bridge.** The caller refuses to start when the bridge cannot be reached, when it refuses the
   token, when a tool `Allow` names is not served, or when a contract hash differs. With `*`, the caller bridges what
   it selected and the bridge serves. The step is shared by `orkeon` (run, validate, scripts, eval) and `orkeon-host`
   (a hosted service right after the MCP servers, exit 78 on a refusal).
5. **The contract hash** is the SHA-256 of the input schema, canonical and without its descriptions (prose, which may
   name what a host holds), and of the access table: the class without arguments and the class of each value of every
   enumerated top-level parameter. `ToolContract.Version` is optional and most tools set none.
6. **The caller keeps judging.** The proxy wraps the caller's own instance: name, description, schema, `Access`,
   `AccessFor` and `UsesFileSystem` stay local, so the gate, the guardian, the truncation and the sanitizer of the
   caller judge the call and its result. Only the call is remote. The bridge runs the tool raw and records the call in
   its own audit under the caller `bridge`.
7. **A lost bridge is a failed call.** Once started, a call the bridge cannot answer fails with the reason; the next
   call tries again. A bridged tool never runs locally. Cancelling a call aborts its request and cancels it on the
   bridge.
8. **The server is guarded.** It listens on loopback by default; any other host requires https. The token comes from
   the environment variable `TokenEnvVar` names, never from a file, and is compared in fixed time. A request carrying an
   `Origin` header is refused, as is a body over `MaxRequestBytes`.
9. **A caller's run is a run on the bridge.** A call carries the caller's id and the id of its agent run; the bridge
   keeps one `AgentRunScope` per caller run and runs every call of that run in it, so "one browser per run" means what
   it means locally. The caller ends that scope with `POST end-run` when its own scope ends, and beats every third of
   `Orkeon:Bridge:HeartbeatWindow` (30 s) while it has live runs; a caller silent past the window loses its runs, and
   their browsers, at once. A call outside any run carries no run id.
10. **A path stays the caller's.** A call of a tool that uses the file system (and does not act on the workspace)
    carries the caller's agent-facing mounts and the files its string arguments name. The bridge presents them in a
    scratch folder at the caller's virtual paths for the length of the call (a folder its path validator allows, as it
    allows the sandbox's), sends back what the tool wrote there, and removes it; the caller writes those files through
    its own file system, under the rights of its run. `MaxFileBytes` (16 MiB) bounds each direction.
11. **The bridge is a command.** `orkeon bridge serve` builds the host `orkeon mcp serve` builds and serves until
    Ctrl+C or SIGTERM; no setting starts it, and `orkeon-host` is a caller only.

## Consequences

- A crew in a container can drive the user's browser or chat client with the pipeline it would apply to a local tool.
- Both machines must run the same version of each bridged tool; a mismatch refuses the start rather than letting the
  caller judge a call by a schema the bridge does not apply.
- The settings of a bridged tool are the bridge's: the caller logs a section of its own (`Orkeon:Tools:<Tool>`) that no
  longer applies.
- A result the bridge returns travels as JSON: a typed result reaches the caller's pipeline as its JSON rendering rather
  than its `ToString()`.
- A connection lost in mid-run ends the run on the bridge within the window, without a grace delay: a caller that
  comes back starts new scopes, and its browser pages are gone.
- How to put both sides in place is the guide [Orkeon in a container, the browser on the desktop](../guides/tool-bridge.md).
