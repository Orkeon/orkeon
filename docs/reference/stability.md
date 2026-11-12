> 🇫🇷 [Version française](../fr/reference/stability.md)

> **See also**: [Back to index](../INDEX.md)

# Stability and versioning policy

Orkeon follows [Semantic Versioning 2.0](https://semver.org/). The version lives in
`src/Directory.Build.props` alone. This page states what the 1.x line commits to; the
contributor-side procedure is in [CONTRIBUTING](../../CONTRIBUTING.md#versioning-and-api-stability).

## What a release may change

| Release | May change | May not change |
|---|---|---|
| **Patch** (`1.x.y`) | Bug fixes, documentation, internal code, performance. | Any public member outside `[Experimental]`; the documented behaviour. |
| **Minor** (`1.x`) | Adds public members, tools, providers, options and settings; deprecates members with `[Obsolete]`; changes anything outside the commitment (below). | Removes or changes the signature of a member of the frozen surface, except under the third-party clause (below). |
| **Major** (`2.0`) | Removes what was deprecated; breaks the frozen surface. | — |

Every breaking change is called out in the `CHANGELOG.md` with a migration note.

## The frozen surface

The commitment is recorded in the repository, not in prose: every public member listed in
the `PublicAPI.Shipped.txt` of a published package is frozen for the whole 1.x line.
`Microsoft.CodeAnalysis.PublicApiAnalyzers` checks it at build time, and an undeclared
public change fails the build (`RS0016` and `RS0017` are errors).

- **A removal or a signature change waits for 2.0.** Editing or removing a line of
  `PublicAPI.Shipped.txt` is, by definition, a breaking change.
- **Additions flow through `PublicAPI.Unshipped.txt`.** A new public member is declared
  there; at release, the entries move to `PublicAPI.Shipped.txt`, and from then on they are
  frozen. `scripts/check-release-readiness.py` refuses a release while any `Unshipped`
  file holds an entry or the `[Unreleased]` section of the CHANGELOG is not empty.

## Types of a third-party library

A few public members carry in their signature a type of a library Orkeon builds on:

- **Jint**, the script engine: the members scripts reach on the runtime objects of
  `Orkeon.Scripting` and `Orkeon.Cli.Commands.Scripting` (a `JsValue` parameter, for one);
- **TreeSitter.DotNet**: `ILanguageAdapter`, the extension point for a new language, and the
  built-in adapters of `Orkeon.Analysis`, which take a `TreeSitter.Node`;
- **Terminal.Gui**: the views of `Orkeon.Cli.TerminalGui`, which derive from its `View`.

Such a member follows the major version of that library, not Orkeon's: when Orkeon moves to
a new major of the dependency, the member may change in a minor Orkeon release, and the
CHANGELOG says so with a migration note. Every member whose signature carries no such type
stays under the commitment above.

## Outside the commitment

- **Experimental APIs**: types marked `[Experimental]` may change or disappear in any
  release, minors included. Referencing one is a compile error with its id, which you
  suppress to opt in (see [Experimental APIs](./experimental-apis.md)).

  | ID | Area |
  |---|---|
  | `ORKEXP001` | A2A (agent-to-agent protocol) |
  | `ORKEXP002` | Autonomous orchestration |
  | `ORKEXP003` | Corrective RAG |
  | `ORKEXP004` | MCP integration |
  | `ORKEXP005` | Per-model capabilities |
  | `ORKEXP006` | Host HTTP security |
  | `ORKEXP007` | Run history ports |

- **Internal types**: anything not listed in a `PublicAPI.Shipped.txt`.
- **The run event protocol** (`--events jsonl`) is a versioned protocol: every line carries
  its protocol version in `v` (see [The run event bus](../architecture/run-event-bus.md)).
  It is versioned on its own, apart from the package versions.

## Deprecation

A public member that is to go is marked `[Obsolete]`, naming its replacement in the
message, in a minor release, and is removed no earlier than the next major.

## Supported versions

The latest 1.x minor receives fixes. Security fixes follow the table in
[SECURITY.md](../../SECURITY.md).
