---
title: Orkeon
---

> 🇫🇷 [Version française](index.fr.md)

# <img src="docs/assets/orkeon-mascot.png" alt="Orkeon mascot — a curious chameleon" width="96" align="absmiddle"> Orkeon

**Build and orchestrate AI agent teams — describe them in declarative YAML, programmatic TypeScript (`.ork.ts`), or pure C#; a single full-.NET stack executes them all**

Orkeon is a C# framework for creating and managing collaborative AI agent teams that tackle
complex, multi-step tasks using large language models. Agents are organized into crews, each
with a defined role, goal, and toolset, and work together through one of six orchestration
strategies (sequential, hierarchical, parallel, consensual, graph, or autonomous).

## Start here

- [**Bootstrap**](docs/getting-started/bootstrap.md) — your first crew, running in minutes
- [**Overview**](docs/getting-started/overview.md) — the concepts: agents, tasks, crews, tools
- [**Documentation**](docs/INDEX.md) — the full guide map: architecture, orchestration, tools, reference
- [**API Reference**](api/index.md) — generated from the shipped assemblies

## Install

```bash
dotnet add package Orkeon.Domain --prerelease
dotnet add package Orkeon.Application --prerelease
dotnet add package Orkeon.Infrastructure --prerelease
```

The scripting CLI ships as a .NET tool:

```bash
dotnet tool install -g orkeon --prerelease
orkeon run crew.ork.ts
```

See the [installation section of the README](README.md) for the other packages, which are
published to GitHub Packages rather than nuget.org.

## Project

- [README](README.md) — the full presentation, with the same crew written three ways
- [CHANGELOG](CHANGELOG.md) — what shipped in each version
- [Contributing](CONTRIBUTING.md) · [Support](SUPPORT.md) · [Security](SECURITY.md)
- [Source on GitHub](https://github.com/Orkeon/orkeon) · [Releases](https://github.com/Orkeon/orkeon/releases)

This site documents the tagged version it was built from; the documentation is published in
[English](docs/INDEX.md) and [French](docs/fr/INDEX.md).
