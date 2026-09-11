---
title: Orkeon
---

> 🇫🇷 [Version française](index.fr.md)

# <img src="docs/assets/orkeon-mascot.png" alt="Orkeon mascot — a curious chameleon" width="96" align="absmiddle"> Orkeon

**AI agent teams that stay inside the lines — every file, endpoint and budget an agent may touch is declared, then enforced. Described in YAML, TypeScript (`.ork.ts`) or C#; one .NET runtime executes all three.**

An agent let loose on a repository writes where it should not, because nothing is there to
stop it. Orkeon puts the boundary in front of the model: a virtual file system where every
path is a declared mount with declared rights, a sandbox for code, an execution budget for
autonomy — and a standalone Roslyn analyzer (`Orkeon.Compliance.Vfs`) that makes raw
`System.IO` a compile error in your own code. Around it, a complete .NET framework for crews
of agents: six orchestration strategies, 14 LLM providers, memory, RAG, code analysis. It
runs on a local model with no API key — see the two-minute start in the
[README](README.md#try-it-in-two-minutes--no-api-key).

## Start here

- [**Bootstrap**](docs/getting-started/bootstrap.md) — your first crew, running in minutes
- [**Overview**](docs/getting-started/overview.md) — the concepts: agents, tasks, crews, tools
- [**Documentation**](docs/INDEX.md) — the full guide map: architecture, orchestration, tools, reference
- [**API Reference**](api/index.md) — generated from the shipped assemblies

## Install

```bash
dotnet add package Orkeon --prerelease        # the complete framework in one package
dotnet add package Orkeon.Tools --prerelease  # optional: the built-in tool families
```

The scripting CLI ships as a .NET tool:

```bash
dotnet tool install -g Orkeon.Scripting.Cli --prerelease
orkeon run crew.ork.ts
```

See the [publication matrix](docs/reference/publication-matrix.md) for the full lineup
(the opt-in ONNX reranker and local-embeddings packages included) and the
[installation section of the README](README.md) for the CLI and container channels.

## Project

- [README](README.md) — the full presentation, with the same crew written three ways
- [CHANGELOG](CHANGELOG.md) — what shipped in each version
- [Contributing](CONTRIBUTING.md) · [Support](SUPPORT.md) · [Security](SECURITY.md)
- [Source on GitHub](https://github.com/Orkeon/orkeon) · [Releases](https://github.com/Orkeon/orkeon/releases)

This site documents the tagged version it was built from; the documentation is published in
[English](docs/INDEX.md) and [French](docs/fr/INDEX.md).
