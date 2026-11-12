# Orkeon.Constants.Cli

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET.

**Orkeon.Constants.Cli** declares the option names Orkeon's runners accept — `--settings`,
`--mount`, `--llm-log`, … — as bare names, without their leading dashes. It **depends on nothing**
([ADR-009](https://github.com/Orkeon/orkeon/blob/main/docs/adr/ADR-009-shared-constants-satellites.md)).

It exists because the same grammar was declared three times: the YAML runner's options, the
scripting runner's options, and the prediction of the command line a client is about to
spawn. A client composes an argument list for a process it does not reference; a flag renamed on one
side and not the other produces a launch that fails on an unrecognised option, at the one moment a
user has no console to read.

## Install

```bash
dotnet add package Orkeon.Constants.Cli
```

See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md) for the rest of the lineup.

## Contents

| Type | What it declares |
|---|---|
| `RunOptionNames` | The option names, bare (`settings`, not `--settings`), as `[Option(...)]` needs them — plus `Flag(name)` to compose the command-line spelling. |
| `RunExitCodes` | The exit codes `orkeon run` adds to 0, 1, 2 and 130: `WaitingForInput` (75, `EX_TEMPFAIL`) — the run stopped on a question to a human and waits for the answer. |
| `SessionStateKeys` | The keys of the REPL's session state the console reads or writes, shared with a product's scripted commands (`default_permission_mode`). |

## License

MIT
