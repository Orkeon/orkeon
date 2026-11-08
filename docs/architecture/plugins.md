> 🇫🇷 [Version française](../fr/architecture/plugins.md)

# Plugin System

> **Status**: v1 (workstream R1.6). Assembly `Orkeon.Plugins`
> (`src/plugins/Orkeon.Plugins`) — **not shipped as a NuGet package**; see
> [Building a plugin project](#building-a-plugin-project).
> The original specification (permissions, hot-reload, per-plugin
> configuration) remains a roadmap; the `plugin.json` manifest is shipped — see `src/plugins/Orkeon.Plugins/SPECIFICATION.md`.

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
- The manifest names the plugin (`name`, cross-checked with `Name`, and `version`, with `Version`). The
  entry assembly may contain other `IOrkeonPlugin` types: they are constructed (`IOrkeonPlugin.Name` is an instance
  property, so the loader must build them to match the name), but their `ConfigureServices` is never called.
  A constructor that throws fails the load.
- On the plugin project side: reference the contracts **without copying them** next to the plugin
  (shipping its own `Orkeon.Plugins.dll` breaks type identity — the loader
  detects this case and reports it explicitly) — see below.

### Building a plugin project

`Orkeon.Plugins` is **not distributed as a NuGet package of its own**. Its csproj sets
`IsPackable=false` and the [publication matrix](../reference/publication-matrix.md#discontinued-packages)
lists the id under *Discontinued packages*; the assembly ships inside the `Orkeon` umbrella package,
one of its thirteen embedded assemblies (`src/packaging/Orkeon/Orkeon.csproj`), so a plugin project
references `Orkeon` (the `orkeon-plugin` template does). `dotnet add package Orkeon.Plugins`
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

On the host side, `orkeon-repl` is the one shipped binary that loads plugins: when its settings carry a `Plugins`
section, it calls `AddOrkeonPlugins(fileSystem, configuration)` while it composes its container, and a plugin's tools
reach its commands and the crews they run like the built-in ones (a plugin tool named like a built-in one refuses the
start: the tool registry names both). Discovery reads a file system of its own, built from the declared mounts
(`Orkeon:FileSystem:Mounts`, and `:InternalMounts` for a folder agents must not see): the registry's checks hold — the
mount, its rights, no path out of it — but not the agents' path validator, which refuses every `.dll` whatever the
mount. `orkeon run` and `orkeon-host` load no plugin; another host calls `AddOrkeonPlugins(...)` itself, in-tree or
against your own clone.

## Discovery (VFS)

Discovery goes through `IFileSystemService`: `OrkeonPluginsOptions.Directory` is a
**virtual path** (e.g. `/plugins`) that must resolve within a configured mount. One layout is
recognized, at the first level only: **a sub-folder per plugin**, holding `plugin.json` and the
entry assembly it names.

```
/plugins/
├── Stray.dll                     # flat: ignored, never loaded
└── WeatherPlugin/
    ├── plugin.json               # the manifest, read before the assembly is loaded
    ├── WeatherPlugin.dll         # the entry assembly the manifest names
    ├── WeatherPlugin.deps.json   # drives the resolution of the private dependencies
    └── Newtonsoft.Json.dll       # private dependency, never treated as a plugin
```

The entry assembly must carry the `.dll` extension and its file name must satisfy `SearchPattern`
(`*.dll` by default, a simple case-insensitive glob). A sub-folder without `plugin.json` is refused
(`PluginManifestException`) when it holds any `.dll`, and ignored otherwise; an entry assembly that misses
`SearchPattern` or that the mount rights deny is refused too, not skipped. The physical path resolution required by the
`AssemblyLoadContext` APIs uses the VFS mechanism
(`IFileSystemService.ResolveAndValidate` with `FileAccessRights.Read`, mount checks included);
a file rejected by the mount policy is simply excluded. Error messages from
the plugin system only reference virtual paths.

- `Directory` must start with `/` — anything else throws an `ArgumentException` at the call.
- A directory that does not exist loads **nothing, silently**: no plugin, no failure.
- Candidates are loaded in ordinal order of their virtual path, so the registration order
  (and therefore "last registration wins" between two plugins) is deterministic.
- The enumeration does not follow symbolic links.

## The manifest (`plugin.json`)

Every plugin carries one, the plugins of the repository included. It is read through the VFS
(`IFileSystemService`, a virtual path) before the assembly is loaded:

```json
{
  "name": "acme.weather-tools",
  "version": "1.0.0",
  "orkeonVersion": "[1.0.0-rc.1,2.0.0)",
  "entryAssembly": "WeatherPlugin.dll",
  "tools": [ "weather_forecast" ]
}
```

| Field | Meaning |
|---|---|
| `name` | Must equal `IOrkeonPlugin.Name`. |
| `version` | Must equal `IOrkeonPlugin.Version`. |
| `orkeonVersion` | NuGet-style range the running Orkeon must fall in: `1.0.0` (that version or later), `[1.0.0]`, `[1.0.0-rc.1,2.0.0)`, `(,2.0.0]`, `*`. The build metadata (`+sha`) of the running version is ignored; a pre-release sorts below its release. |
| `entryAssembly` | File name of a `.dll` of the plugin folder: no path. |
| `entryType` | Optional. Full name of the `IOrkeonPlugin` type to activate. Without it, an assembly with one plugin type activates that one; with several, all are activated to read their `Name`. |
| `tools` | Names of the `IBaseTool` descriptors the plugin registers (`IBaseTool.Name`); may be empty. Other registrations (services, hosted services, providers) are not covered by `tools`. |

`PluginManifestException` (a `PluginLoadException`, so `ContinueOnError` covers it) refuses the plugin
when the manifest is absent or malformed, a required field is missing, the entry assembly is
missing, `orkeonVersion` excludes the running Orkeon, the manifest names no plugin of the entry
assembly or disagrees with it on the version, or the plugin registers an `IBaseTool` whose name is
not in `tools`. A refused plugin leaves no registration behind: the service collection is restored as the host had it
(a `Replace` or `RemoveAll` of a host registration included), and a `ConfigureServices` that throws is rolled
back the same way and surfaces as a `PluginLoadException`. A tool registered as an instance is checked at
registration. A tool registered by type, factory or key is checked by the host's own provider when it
builds the tool (once, with the services the host registers after `AddOrkeonPlugins`, such as `ILogger<T>`):
an undeclared name throws `PluginManifestException` naming the plugin, and an exception from the tool's
code surfaces as a `PluginLoadException` with the cause inside. That check happens when the host resolves
the tools, after `ContinueOnError` has done its work. A tool declared but not registered is not an error.

A minimal plugin lives in the tests (`tests/plugins/Orkeon.Plugins.Tests/Fixtures/SamplePlugin/`): its
`plugin.json` and `SamplePlugin`, which registers the one tool the manifest declares.

## Loading and isolation

Each plugin assembly is loaded into its own `PluginLoadContext`:

- **collectible** (`isCollectible: true`) → unloadable;
- dependencies resolved by `AssemblyDependencyResolver` (the plugin's `.deps.json`) →
  each plugin can ship **its own versions** of dependencies, native libraries included
  (`LoadUnmanagedDll` goes through the same resolver);
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

```json
{
  "Plugins": {
    "Directory": "/plugins",
    "SearchPattern": "Acme.*.dll",
    "ContinueOnError": false,
    "SharedAssemblyPrefixes": [ "Acme.Contracts." ]
  }
}
```

The keys are the `OrkeonPluginsOptions` properties; every one is optional (defaults:
`/plugins`, `*.dll`, `false`). Entries under `SharedAssemblyPrefixes` are **added** to the
three defaults (the configuration binder appends to the existing list) — narrowing the
defaults takes the delegate overload. The options are consumed at the call and are not
registered as `IOptions<OrkeonPluginsOptions>`. The `/plugins` mount itself is the host's
business: declare it like any other (`Orkeon:FileSystem:Mounts`, read-only is enough).

Specifics:

- discovery and loading are **immediate** (at the time of the call): plugins
  must contribute their registrations to the `IServiceCollection` before the
  provider is built;
- a plugin whose entry assembly has no `IOrkeonPlugin` named by the manifest is refused and its
  context unloaded;
- `ContinueOnError = true` records failures in `IPluginRegistry.Failures` instead
  of throwing `PluginLoadException` (without rolling back the registrations already
  contributed by the faulty plugin);
- `IPluginRegistry` (singleton) exposes `Assemblies`, `Plugins` (name/version) and
  `Failures` for introspection.

## v1 limitations

| Out of scope in v1 | Lead (SPECIFICATION.md) |
|---|---|
| Sandbox / permission model | `PluginSecurityManager`, `PluginSandbox` |
| Hot-reload | `ReloadPluginAsync` |
| Persisted per-plugin configuration | `IPluginConfigurationStore` |
| Symlinks in the plugin directory | not followed |

---

> **See also**: [Security, resilience and plugins](./security.md) ·
> [Opt-in subsystems](../reference/opt-in-subsystems.md) ·
> [Back to index](../INDEX.md)
