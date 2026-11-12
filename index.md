---
title: Orkeon
---

> 🇫🇷 [Version française](index.fr.md)

# Orkeon

**AI agent teams that stay inside the lines — every file, endpoint and budget an agent may use is declared, then enforced by the runtime. Described in YAML, TypeScript (`.ork.ts`) or C#; one .NET runtime executes all three, and the team you tuned is the team you integrate, without a rewrite.**

<p align="center">
  <img src="docs/assets/orkeon-mascot.png" alt="Orkeon mascot — a curious chameleon" width="320">
</p>

Orkeon is an open-source framework for building and orchestrating teams of AI agents in
.NET. It rests on two convictions. **Trust is built in the runtime, not in the prompt**: a
virtual file system where every path is a declared mount with declared rights, a sandbox for
code, an execution budget for autonomy, sensitive capabilities that stay closed until a
person opens them — and a standalone Roslyn analyzer (`Orkeon.Compliance.Vfs`) that makes raw
`System.IO` a compile error in your own code. **What you tune is what you deploy**: the
runtime that ran the first trial is the one inside the final application, so a team is never
rewritten to go from the trial to the product. Around it, a complete .NET framework for crews
of agents: six orchestration strategies, 16 LLM providers, memory, RAG, code analysis. It
runs on a local model with no API key — see the two-minute start in the
[README](README.md#try-it-in-two-minutes--no-api-key).

Every door opens on the same runtime: [Orkeon Studio](https://github.com/Orkeon/orkeon-studio),
a desktop application for Windows with no code and no terminal;
[Orkeon Workshop](https://github.com/Orkeon/orkeon-workshop), a ready-made workshop to design,
test and measure teams; [Orkeon Code](https://github.com/Orkeon/orkeon-code), a coding
assistant in the terminal; the `orkeon` command line; and the `Orkeon` NuGet package.

## Start here

- [**Run your first example**](docs/getting-started/run-your-first-example.md) — a bundled crew, running from a clone in minutes
- [**Three ways to run Orkeon**](docs/getting-started/three-ways-to-run-orkeon.md) — from source, a release binary or the container
- [**Bootstrap**](docs/getting-started/bootstrap.md) — embed Orkeon in your own .NET host: packages, DI wiring, running a crew
- [**Give your agents a mailbox**](docs/getting-started/give-your-agents-a-mailbox.md) — Gmail, Outlook.com or your own server: an agent reads, sorts and drafts replies; sending stays closed until you open it
- [**Overview**](docs/getting-started/overview.md) — the concepts: agents, tasks, crews, tools
- [**Documentation**](docs/INDEX.md) — the full guide map: architecture, orchestration, tools, reference
- [**API Reference**](api/index.md) — generated from the shipped assemblies

## Install

```bash
dotnet add package Orkeon        # the complete framework in one package
dotnet add package Orkeon.Tools  # optional: the built-in tool families
```

The scripting CLI ships as a .NET tool:

```bash
dotnet tool install -g Orkeon.Scripting.Cli
orkeon run crew.ork.ts
```

See the [publication matrix](docs/reference/publication-matrix.md) for the full lineup
(the opt-in ONNX reranker, local-embeddings, Agent Framework and Aspire packages included) and the
[installation section of the README](README.md) for the CLI and container channels. To
update — to the latest release, or to the latest `main` between two releases — see
[Update Orkeon](docs/getting-started/three-ways-to-run-orkeon.md#update-orkeon).

## Project

- [README](README.md) — the full presentation: why Orkeon, what a team does, how it works, which door to take, what Orkeon guarantees and what it does not
- [CHANGELOG](CHANGELOG.md) — what shipped in each version
- [Contributing](CONTRIBUTING.md) · [Support](SUPPORT.md) · [Security](SECURITY.md)
- [Source on GitHub](https://github.com/Orkeon/orkeon) · [Releases](https://github.com/Orkeon/orkeon/releases)

This site documents the tagged version it was built from; the documentation is published in
[English](docs/INDEX.md) and [French](docs/fr/INDEX.md).
