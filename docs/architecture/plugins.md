> 🇫🇷 [Version française](../fr/architecture/plugins.md)

# Plugin System

> **Status**: v1 (workstream R1.6). Assembly `Orkeon.Plugins`
> (`src/plugins/Orkeon.Plugins`) — **not shipped as a NuGet package**; see
> [Building a plugin project](#building-a-plugin-project).
> The original specification (manifest, permissions, hot-reload, per-plugin
> configuration) remains a roadmap — see `src/plugins/Orkeon.Plugins/SPECIFICATION.md`.

The plugin system lets you drop third-party assemblies into a directory and let
them contribute services (`IBaseTool` tools, LLM providers, memory providers,
or any other service) to the host's dependency injection container —
without recompiling the host.

## ⚠️ Trust boundary — read this first

**Loading a plugin means executing arbitrary code with all the privileges of the
host process.** There is **no sandbox in v1**, and an `AssemblyLoadContext` is an
*isolation* mechanism (independent dependency versions, unloadability),
**not a security boundary**: a plugin can read environment variables,
open network connections, access the physical disk (outside the VFS), etc.

Minimal operating rules:

- Only point `OrkeonPluginsOptions.Directory` to a directory whose **content
  is trusted** (code review, signature/fingerprint verified out-of-band, controlled
  supply chain).
- Protect the plugin directory against **writes** by untrusted actors
  (dropping a DLL = code execution at the next startup).
- A plugin's `ConfigureServices` runs **at startup**, before the container is
  built: a malicious plugin does not even need its services to be resolved.
- When in doubt, do not load: prefer `ContinueOnError = false` (default) so
  that an unexpected assembly fails the startup rather than being ignored.

## `IOrkeonPlugin` contract

```csharp
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Plugins;

public sealed class WeatherPlugin : IOrkeonPlugin
{
    public string Name => "acme.weather-tools";   // stable name
    public string Version => "1.0.0";             // informational, SemVer recommended

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<Orkeon.Domain.Tools.IBaseTool, WeatherForecastTool>();
    }
}
```

- Public constructor with no required parameter (instantiation via `Activator`).
- An assembly may contain several implementations; all of them are instantiated.
- On the plugin project side: reference the contracts **without copying them** next to the plugin
  (shipping its own `Orkeon.Plugins.dll` breaks type identity — the loader
  detects this case and reports it explicitly) — see below.

### Building a plugin project

`Orkeon.Plugins` is **not distributed as a NuGet package**. Its csproj sets `IsPackable=false`,
the [publication matrix](../reference/publication-matrix.md#discontinued-packages) lists it under
*Discontinued packages*, and it is not one of the eleven assemblies embedded in the `Orkeon`
umbrella package (`src/packaging/Orkeon/Orkeon.csproj`). `dotnet add package Orkeon.Plugins`
resolves on no feed — NuGet.org and GitHub Packages alike (`NU1101`).

A plugin author builds against it **from source**: clone
[`Orkeon/orkeon`](https://github.com/Orkeon/orkeon) and point a `ProjectReference` at
`src/plugins/Orkeon.Plugins/Orkeon.Plugins.csproj`. The other contracts a plugin touches
(`IBaseTool`, `IFileSystemService`, the provider interfaces) do come from the published `Orkeon`
package, so only the `IOrkeonPlugin` entry point needs the source reference.

```xml
<ItemGroup>
  <!-- Contracts only: ExcludeAssets="runtime" keeps Orkeon.Plugins.dll out of the plugin's
       output, so the host's copy remains the single type identity. -->
  <ProjectReference Include="../orkeon/src/plugins/Orkeon.Plugins/Orkeon.Plugins.csproj"
                    ExcludeAssets="runtime" />
  <PackageReference Include="Orkeon" ExcludeAssets="runtime" />
</ItemGroup>
<PropertyGroup>
  <!-- emits the .deps.json used to resolve the private dependencies -->
  <EnableDynamicLoading>true</EnableDynamicLoading>
</PropertyGroup>
```

The host side carries the same constraint: none of the binaries Orkeon ships calls
`AddOrkeonPlugins(...)` — no project under `src/` references `Orkeon.Plugins`. The plugin system is activated by a host
you build yourself, in-tree or against your own clone.

## Discovery (VFS)

Discovery goes through `IFileSystemService`: `OrkeonPluginsOptions.Directory` is a
**virtual path** (e.g. `/plugins`) that must resolve within a configured mount. Two
layouts are recognized, at the first level only:

```
/plugins/
├── MyPlugin.dll                  # "flat" layout
└── WeatherPlugin/
    ├── WeatherPlugin.dll         # "folder per plugin" layout (<dir>/<dir>.dll)
    ├── WeatherPlugin.deps.json   # drives the resolution of the private dependencies
    └── Newtonsoft.Json.dll       # private dependency, never treated as a plugin
```

Candidates must carry the `.dll` extension and satisfy `SearchPattern`
(`*.dll` by default). The physical path resolution required by the
`AssemblyLoadContext` APIs uses the VFS mechanism
(`IFileSystemService.ResolveAndValidate`, mount checks + `FileAccessRights`);
a file rejected by the mount policy is simply excluded. Error messages from
the plugin system only reference virtual paths.

## Loading and isolation

Each plugin assembly is loaded into its own `PluginLoadContext`:

- **collectible** (`isCollectible: true`) → unloadable;
- dependencies resolved by `AssemblyDependencyResolver` (the plugin's `.deps.json`) →
  each plugin can ship **its own versions** of dependencies;
- assemblies whose simple name starts with a prefix from
  `SharedAssemblyPrefixes` (`Orkeon.`, `Orkeon.Rag.Abstractions`, `Microsoft.Extensions.` by default — the middle entry is explicit so RAG contract types keep inter-ALC identity even for hosts that narrow the defaults) are
  **never** resolved in the plugin context: they unify with the host
  context, guaranteeing a single identity for `IOrkeonPlugin`, `IBaseTool`,
  `IServiceCollection`, etc.

Unloading: `PluginRegistry.UnloadAll()` (or `Dispose()`) initiates the unloading of
all contexts. Unloading an ALC is **cooperative**: it only completes once
no object originating from the plugin is reachable anymore — in practice, after the
`Dispose()` of the `ServiceProvider` consuming the plugin's services. Since the registry
is registered as a singleton *instance*, the container does not dispose it:
unloading remains an explicit decision by the host.

## DI activation (opt-in)

Consistent with the [opt-in subsystems](../reference/opt-in-subsystems.md) pattern
(R4.9): **nothing is loaded by default**, neither by `AddOrkeonApplication()` nor by
`AddOrkeonInfrastructure()`. The host calls explicitly, exactly once:

```csharp
using Orkeon.Plugins;

// The IFileSystemService is built at bootstrap, before DI
// (same step as the VFS mount provisioning).
services.AddOrkeonPlugins(fileSystem, options =>
{
    options.Directory = "/plugins";
    options.SearchPattern = "*.dll";
    options.ContinueOnError = false;   // fail fast (default)
});

// Or binding from the configuration ("Plugins" section):
services.AddOrkeonPlugins(fileSystem, configuration);
```

Specifics:

- discovery and loading are **immediate** (at the time of the call): plugins
  must contribute their registrations to the `IServiceCollection` before the
  provider is built;
- a managed assembly with no `IOrkeonPlugin` implementation is ignored and its context
  immediately unloaded;
- `ContinueOnError = true` records failures in `IPluginRegistry.Failures` instead
  of throwing `PluginLoadException` (without rolling back the registrations already
  contributed by the faulty plugin);
- `IPluginRegistry` (singleton) exposes `Assemblies`, `Plugins` (name/version) and
  `Failures` for introspection.

## v1 limitations

| Out of scope in v1 | Lead (SPECIFICATION.md) |
|---|---|
| Sandbox / permission model | `PluginSecurityManager`, `PluginSandbox` |
| `plugin.json` manifest (out-of-code metadata) | `PluginManifest` |
| Hot-reload | `PluginLoader.Reload` |
| Persisted per-plugin configuration | `IPluginConfigurationStore` |
| Symlinks in the plugin directory | not followed |

---

> **See also**: [Security, resilience and plugins](./security.md) ·
> [Opt-in subsystems](../reference/opt-in-subsystems.md) ·
> [Back to index](../INDEX.md)
