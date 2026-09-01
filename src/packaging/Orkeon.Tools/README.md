# Orkeon.Tools

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Tools** bundles the built-in agent tool families in one package: file system, web and HTTP (scraping, APIs, search), structured data (CSV, PDF, XLSX, XML, JSON, email, and database queries across SQLite, SQL Server, PostgreSQL, MySQL, MongoDB, Neo4j, Gremlin), secure code execution, EventHub messaging, RAG search and ingestion, and the RaggableTree codebase-analysis tools. It is published separately from [Orkeon](https://www.nuget.org/packages/Orkeon) so that consumers who never use these tools do not inherit their third-party dependencies (database drivers, PDF and spreadsheet libraries).

## Install

```
dotnet add package Orkeon.Tools --prerelease
```

The package depends on `Orkeon`, which is restored automatically.

## Documentation

- [Tools inventory](https://github.com/Orkeon/orkeon/blob/main/docs/tools/inventory.md)
- [Overview](https://github.com/Orkeon/orkeon/blob/main/docs/getting-started/overview.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
