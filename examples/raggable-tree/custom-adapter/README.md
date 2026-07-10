# RaggableTree — Custom language adapter

> Shows how to teach RaggableTree a new language by implementing
> `ILanguageAdapter`. Here a `JavaAdapter` adds Java support on top of the
> built-in TypeScript adapter.

## What it does

- Implements `JavaAdapter` (`ILanguageAdapter`) with the Tree-sitter declaration
  queries and node/statement mappings for Java classes, interfaces, enums,
  methods, constructors, and fields.
- Registers it alongside the built-in `TypeScriptAdapter` and indexes a directory
  containing a mix of TypeScript and Java.
- Prints the combined node count.

Use this as the starting point for supporting any language Tree-sitter can parse.
See the [RaggableTree guide](../../../docs/architecture/raggable-tree.md) for the
`ILanguageAdapter` contract.

## Prerequisites

- .NET SDK ≥ 10.0.300
- Uses the `TreeSitter.DotNet` package (restored automatically).

No API key or `appsettings` needed — indexing is fully local.

## Required data

None to provide. The example indexes the directory you pass (or the current
directory by default); point it at a tree that contains `.java` and `.ts` files
to see both adapters contribute.

## Run it

```bash
# Index the current directory with TypeScript + Java adapters
dotnet run --project examples/raggable-tree/custom-adapter

# Or index a specific directory
dotnet run --project examples/raggable-tree/custom-adapter -- /path/to/a/codebase
```

## Expected output

```
Indexed <n> nodes across TypeScript + Java.
JavaAdapter provides the 7 Tree-sitter queries and node/statement mappings.
Register it via DI to see it picked up by AddRaggableTree() automatically.
```

## Approx. duration & cost

- **Duration**: seconds for a small tree.
- **Cost**: none — no LLM calls.
</content>
