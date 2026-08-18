# Orkeon.Plugins

Plugin system for [Orkeon](https://github.com/Orkeon/orkeon): drop third-party
assemblies in a directory and let them contribute tools, LLM providers, or any other
service to the host's dependency-injection container — no recompilation of the host.

## Install

```
dotnet add package Orkeon.Plugins --prerelease
```

> This package is published on the [GitHub Packages feed](https://github.com/orgs/Orkeon/packages); add the feed as a NuGet source first — see the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).


> **Trust boundary — read this first.** Loading a plugin executes arbitrary code with
> the **full privileges of the host process**. There is **no sandbox** in v1, and an
> `AssemblyLoadContext` is an *isolation* mechanism (independent dependency versions,
> unloadability), **not a security boundary**. Only load plugins from sources you trust,
> and keep the plugin directory write-protected from untrusted actors.
> Full guidance: `docs/architecture/plugins.md`.

## What v1 provides

- **`IOrkeonPlugin`** — minimal contract: `Name`, `Version`,
  `ConfigureServices(IServiceCollection)`.
- **Directory-based discovery** — scans a configurable directory (a VFS virtual path)
  for `*.dll`, in two layouts: flat (`/plugins/MyPlugin.dll`) or folder-per-plugin
  (`/plugins/MyPlugin/MyPlugin.dll`, private dependencies alongside).
- **Isolated loading** — one *collectible* `AssemblyLoadContext` per plugin assembly,
  dependencies resolved through `AssemblyDependencyResolver` (the plugin's
  `.deps.json`). Contract assemblies (`Orkeon.*`, `Microsoft.Extensions.*` by default)
  are unified with the host so type identities match.
- **Opt-in DI activation** — `AddOrkeonPlugins(...)`, never registered implicitly.
- **Introspection** — `IPluginRegistry` singleton listing loaded assemblies, plugin
  instances, and (with `ContinueOnError`) load failures.

## Writing a plugin

```csharp
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Plugins;

public sealed class WeatherPlugin : IOrkeonPlugin
{
    public string Name => "acme.weather-tools";
    public string Version => "1.0.0";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<Orkeon.Domain.Tools.IBaseTool, WeatherForecastTool>();
    }
}
```

Plugin project file — reference the contracts without copying them next to your plugin
(shipping your own `Orkeon.Plugins.dll` breaks type identity):

```xml
<ItemGroup>
  <PackageReference Include="Orkeon.Plugins" ExcludeAssets="runtime" />
</ItemGroup>
<PropertyGroup>
  <EnableDynamicLoading>true</EnableDynamicLoading> <!-- emits the .deps.json used for resolution -->
</PropertyGroup>
```

## Hosting plugins

```csharp
using Orkeon.Plugins;

// The IFileSystemService is built at bootstrap, before DI (same stage that
// provisions the VFS mounts). "/plugins" is a virtual path inside a mount.
services.AddOrkeonPlugins(fileSystem, options =>
{
    options.Directory = "/plugins";
    options.SearchPattern = "*.dll";
    options.ContinueOnError = false; // fail fast (default)
});

// Or bind from configuration (section "Plugins"):
services.AddOrkeonPlugins(fileSystem, configuration);
```

After `BuildServiceProvider()`, plugin-contributed services resolve like any other, and
`IPluginRegistry` lists what was loaded.

## Not in v1 (future scope)

Manifest files (`plugin.json`), permission model / sandboxing, hot reload, per-plugin
configuration stores. The original design covering those lives in
[SPECIFICATION.md](SPECIFICATION.md).
