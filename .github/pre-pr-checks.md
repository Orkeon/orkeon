# Checks before a pull request is opened or updated

A pull request runs checks that a local `dotnet build` and `dotnet test` do not: a second SDK, CodeQL, a
secret scan. Each one below has already turned a pull request red after a green local run. Run
`bash scripts/pre-pr-check.sh` before every `gh pr create` and before every push to a branch that has an open
pull request; it runs what can run on this machine and names what cannot. A `FAIL` line is refused by the CI for sure; a `note` line lists code to read against the tables below. Add a rule here, and its pattern to
the script, each time the CI refuses something the script let through.

## What the CI runs, and what answers it locally

| CI check | Workflow | Local answer |
|---|---|---|
| `build-test` | `ci.yml` | `dotnet build Orkeon.sln --no-restore --no-incremental -c Release -warnaserror`, then `dotnet test Orkeon.sln --no-build -c Release --filter "Category!=Integration&Category!=Slow"` |
| `build-test-net11` | `ci.yml` | The same build under the .NET 11 SDK, whose analyzers know rules the .NET 10 SDK does not. Not installed here: the script greps the known patterns (below). |
| `CodeQL` / `Analyze (csharp)` | `codeql.yml` | No local run. The check fails on a new alert of level **error**; a new **warning** is not accepted either. The script greps the patterns below; read the rest before pushing. |
| `gitleaks` | `secret-scan.yml` | `gitleaks git --config .gitleaks.toml --redact --no-banner .` when the binary is installed; otherwise the script greps key-shaped literals in the diff. The scan reads the **history**: rewriting the line later does not clear it. |
| `exec-bit` | `file-modes.yml` | `git ls-files -s -- '*.sh'`: every line starts with `100755`. |
| `lint-readmes`, `lint-configs`, `validate-examples` | `examples-ci.yml` | `python3 scripts/check-doc-claims.py`, `bash scripts/check-docs-parity.sh`, `bash scripts/check-scripting-typings.sh` |
| comments | `ci.yml` | `python3 scripts/check-comment-accents.py` |
| `Docs site`, `quickstart`, `rag-eval`, `dependency-review`, `cla` | their own workflows | No local answer; they follow from the above unless a package, a quickstart or a docs link changed. |

## .NET 11 SDK analyzers (errors under `-warnaserror`)

| Rule | Refused | Write |
|---|---|---|
| CA2026 | `JsonDocument.Parse(json).RootElement` used in one expression: the document is never disposed | `JsonElement.Parse(json)`, or `using var document = JsonDocument.Parse(json);` |

## CodeQL rules that have fired (tests are analysed as well as `src/`)

| Rule | Level | Refused | Write |
|---|---|---|---|
| `cs/unsynchronized-static-access` | error | a static member such as `ImmutableDictionary<K, V>.Empty` read in an instance method, in a class where CodeQL sees the same member under a lock elsewhere. Most uses pass; no pattern predicts it | `new Dictionary<K, V> { [k] = v }.ToImmutableDictionary()` at the place it names |
| `cs/dereferenced-value-may-be-null` | warning | `x.P?[...]` then `x.P!.Member` or `x.P[...]` on the next lines | `var p = x.P; Assert.NotNull(p);` then `p` everywhere |
| `cs/local-not-disposed` | warning | `new MemoryStream(...)`, an `HttpResponseMessage`, a `ServiceProvider`, a `Jint.Engine` created inline and never disposed, tests included | `using var` |
| `cs/equality-on-floats` | warning | `value != Math.Floor(value)`, `a == b` on `double` | `double.IsInteger(value)`, or a tolerance |
| `cs/empty-block` | warning | an empty `{ }` (a drained `await foreach`, an empty `catch`) | a comment inside the block saying why it is empty |
| `cs/useless-upcast` | warning | `(string?)null` inside a tuple or an argument | assert the two values apart: `Assert.Null(after)` |
| `cs/xmldoc/missing-summary` | note | a public member documented with `<param>` only | a `<summary>` first |
| `cs/path-combine` | note | `Path.Combine(a, b)` | accepted in tests; in `src/` the VFS rule forbids physical paths anyway |
| `cs/linq/missed-select`, `cs/linq/missed-where`, `cs/coupled-types` | note | a `foreach` that maps or filters at once; a large facade | accepted when the loop throws or reads better; not a reason to restructure |

Read the alerts of a pull request with
`gh api "repos/Orkeon/orkeon/code-scanning/alerts?pr=<n>&state=open&per_page=100"`.

## Secret scan (gitleaks, `generic-api-key` and the provider rules)

- A literal next to a name that holds `key`, `secret`, `token` or `password` is flagged when it looks random:
  an invented environment-variable name (`ORKEON_TEST_451_NO_SUCH_KEY`), a fake identifier (`wrkspc_01Test`),
  a fake key (`sk-test-...`).
- Prefer a value already listed in `.gitleaks.toml`, or build the value at run time
  (`"sk-" + new string('a', 24)`).
- Otherwise add the exact value to the first allowlist of `.gitleaks.toml`, one line per value, never a path,
  in the same commit as the test. A real secret is never allowlisted: it is revoked.

## The rest of the list

- One `--no-incremental` Release build before the push: an incremental build hides CS0419 and analyzer
  errors of projects it did not rebuild.
- A new public member is declared in the project's `PublicAPI.Unshipped.txt` (RS0016).
- A `Jint.Engine` built in a test sets `PromiseTimeout = HangGuards.EnginePromise` (an architecture test
  reads the test sources).
- A setting added or changed: regenerate the catalogue, the reference tables and the sample
  (`UPDATE_PRODUCED_FILES=1` on the `SettingsCatalogFile`, then `SettingsReferencePage|SettingsSampleFile`
  tests), and write its French sentence in `docs/fr/reference/configuration.settings.json`.
- A changed docs page is mirrored in `docs/fr/` in the same commit; `CHANGELOG.md` names the issue.
- After the push, read `gh pr checks <n>` until every check has ended: a pending check is not a green one.
