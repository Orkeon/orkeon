# Orkeon.Scripting

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Scripting** is the TypeScript-syntax scripting DSL (`.ork.ts`): scripts are transpiled with esbuild and executed in a sandboxed Jint runtime, with the full Orkeon surface (agents, crews, tools, FSM, graphs, events, RAG retrieval) exposed through fluent builders.

## Install

```bash
dotnet add package Orkeon.Scripting --prerelease
```

The package embeds the Jint host, the esbuild bundler driver, the DSL typings and `Orkeon.Hosting`, the runner bootstrap of `orkeon run`; it depends on `Orkeon`, `Orkeon.Tools` and `Orkeon.Tools.Embeddings.Local`. esbuild itself is not shipped: put it on `PATH` (`npm install -g esbuild`) or point `ORKEON_ESBUILD_PATH` at a binary. See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Run a crew in-process

```csharp
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Hosting;

var services = new ServiceCollection().AddOrkeonScripting(options =>
{
    options.SettingsPath = "appsettings.json";
    options.Mounts.Add("./out:/output:rw");
});
using var provider = services.BuildServiceProvider();

var output = await provider.GetRequiredService<ICrewScriptRunner>().RunAsync("crew.ork.ts");
```

The script hands its crew off with `globalThis.crew = crew`; a run that cannot start throws `CrewScriptRunException`. Details: [Hosting — running a crew in-process](https://github.com/Orkeon/orkeon/blob/main/docs/reference/hosting.md#running-a-crew-in-process).

## Documentation

- [Scripting DSL](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/scripting.md)
- [Service host — in-process, or under orkeon-host](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/service-host.md#8-in-process-or-under-orkeon-host)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
