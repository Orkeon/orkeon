# Orkeon.Templates

`dotnet new` templates for [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET.

## Install

```
dotnet new install Orkeon.Templates --prerelease
```

## Templates

| Short name | Generates |
|---|---|
| `orkeon-host` | A .NET generic host that runs a crew (`crew.ork.ts`) in its own process, configured from `appsettings.json` |
| `orkeon-plugin` | A plugin: an `IOrkeonPlugin`, one tool and the `plugin.json` that declares it |
| `orkeon-tool` | A class library with one tool (`ToolBase<TRequest, TResponse>`) |

```
dotnet new orkeon-tool -n Acme.Tools
```

A generated project references the Orkeon packages at the version of the template package, written out in
the project file. `orkeon init` is unrelated: it writes the settings file, not a project.

## Documentation

- [Getting started](https://github.com/Orkeon/orkeon/blob/main/docs/getting-started/overview.md)
- [Repository](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
