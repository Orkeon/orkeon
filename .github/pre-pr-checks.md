# Checks before a pull request is opened or updated

A pull request runs checks that a local `dotnet build` and `dotnet test` do not: a second SDK, CodeQL, a
secret scan. Each one below has already turned a pull request red after a green local run. Run
`bash scripts/pre-pr-check.sh` before every `gh pr create` and before every push to a branch that has an open
pull request; it runs what can run on this machine and names what cannot.

- It searches the lines the branch adds to C# files (renamed files included) since its merge base with the
  target (`origin/main`, or the branch named as its argument). A `FAIL` line is a shape the CI refuses for
  sure: fix it before the push. A `note` line lists code to read against the tables below: it may pass.
- Without an option it also runs the exec-bit rule, `check-doc-claims.py`, `check-docs-parity.sh`,
  `check-comment-accents.py` and `check-scripting-typings.sh`. `--full` runs instead every step of the
  `ci.yml` job `build-test` that can run here, in the job's order (the script's header names the ones it
  cannot run; it never runs `dotnet restore`).
- `--self-test` plants one added line per pattern, and the shapes that must not fire, in a throw-away git
  repository under `${TMPDIR:-/tmp}`, and exits non-zero when a pattern stays silent or a negative case fires.
- It stops at once when `grep -P` does not work (the BSD grep of macOS): install GNU grep.

Add a rule here, its pattern to the script and its case to `--self-test`, each time the CI refuses something
the script let through.

## What a pull request into `main` runs

The `main` ruleset (`gh api repos/Orkeon/orkeon/rulesets`) requires `build-test`, `gitleaks`, `exec-bit`,
`dependency-review` and the four `Analyze (...)` jobs. It has no `code_scanning` rule: a CodeQL alert does not
block the merge by itself, and `build-test-net11` is not required. Treat a red `build-test-net11` or `CodeQL`
check as something to fix before asking for a merge all the same. "A new warning is not accepted" is the
project's policy, not a check that enforces it.

| Job | Workflow | What it runs | Local answer | Script | Required |
|---|---|---|---|---|---|
| `build-test` | `ci.yml` | `check-docs-parity.sh`, `check-doc-claims.py`, `test-check-doc-claims.py`, `test-third-party-notices.py`, `check-comment-accents.py`, the tests of the release and apt scripts (`test-*.sh`, `test-smoke-notices.ps1`, `scripts/apt/test-*.sh` and the two apt guards), `check-scripting-typings.sh`, `dotnet restore`, `third-party-notices.py --check`, the Release build with `-warnaserror`, `check-scripting-typings.sh --rollup`, the unit and fast tests, the ONNX tests of `Embeddings.Local` | the same commands, `--no-restore` | `--full`, in this order, without the restore (and without the PowerShell twin when `pwsh` is missing) | yes |
| `build-test-net11` | `ci.yml` | the same build and tests under the .NET 11 SDK, whose analyzers know rules the .NET 10 SDK does not | the SDK is not installed here | greps the known patterns (below) | no |
| `resolve-version-windows` | `ci.yml` | `test-resolve-version.sh` and `install-from-source.ps1` under Windows PowerShell 5.1 and PowerShell 7 | none on Linux | `--full` runs the bash test | no |
| `Analyze (csharp)`, `(javascript-typescript)`, `(python)`, `(actions)` | `codeql.yml` | CodeQL `security-and-quality`, without a build; the alerts show in the `CodeQL` check | no local run | greps the C# patterns below | yes (the analysis, not its alerts) |
| `gitleaks` | `secret-scan.yml` | `gitleaks git --config .gitleaks.toml --redact --no-banner --verbose .`: the whole **history** | the same command, when the binary is installed | gitleaks on the branch's commits; without it, a grep of key-shaped literals | yes |
| `exec-bit` | `file-modes.yml` | every tracked `*.sh` and every text file whose first line is a shebang is `100755` (`scripts/installer-assets/wrapper.sh.tmpl` exempt) | `git ls-files -s` | the same rule, every run | yes |
| `dependency-review` | `dependency-review.yml` | GitHub's dependency review, refusing a high-severity advisory | none | no | yes |
| `lint-configs` | `examples-ci.yml` | `lint-example-configs.py`, `test-examples-catalog.py` | the same commands | no | no |
| `lint-readmes` | `examples-ci.yml` | `lint-example-readmes.py`, `generate-examples-index.sh --check` | the same commands | no | no |
| `validate-examples` | `examples-ci.yml` | `dotnet build examples/Orkeon.Examples.sln -c Release -warnaserror`, `validate-all-examples.sh`, then with `--configs` on the two declarative crews | the same commands, `--no-restore` | no | no |
| `build` | `docs.yml` (Docs site) | the Release build, `docfx metadata`, `docfx build --warningsAsErrors` | docfx when installed | no | no |
| `quickstart` | `quickstart.yml` | the README's two blocks against Ollama, with the tool packed from the checkout | none (Ollama and a model) | no | no |
| `rag-eval` | `rag-eval.yml` | `orkeon eval rag --offline` on `examples/rag/eval/golden.yaml`: the profile comparison and the balanced-profile gate | the two `dotnet run` commands of the workflow | no | no |
| `cla` | `cla.yml` | the CLA signature of a first contribution | none | no | no |

`examples-ci.yml`, `docs.yml`, `quickstart.yml` and `rag-eval.yml` run only when the pull request touches the
paths their `on.pull_request.paths` list.

## .NET 11 SDK analyzers (errors under `-warnaserror`)

| Rule | Refused | Write |
|---|---|---|
| CA2026 | `JsonDocument.Parse(json).RootElement` used in one expression: the document is never disposed | `JsonElement.Parse(json)`, or `using var document = JsonDocument.Parse(json);` |

## CodeQL rules that have fired (tests are analysed as well as `src/`)

| Rule | Level | Refused | Write |
|---|---|---|---|
| `cs/unsynchronized-static-access` | error | a static member such as `ImmutableDictionary<K, V>.Empty` read in an instance method, in a class where CodeQL sees the same member under a lock elsewhere. Most uses pass; no pattern predicts it | `new Dictionary<K, V> { [k] = v }.ToImmutableDictionary()` at the place it names |
| `cs/dereferenced-value-may-be-null` | warning | the same value read with `?.` or `?[` and then with `!.` or `![`: `e.Payload?["agentRole"]`, then `e.Payload!.ContainsKey(...)` a line later. The script notes it when both are within 5 added lines of one file | `var p = x.P; Assert.NotNull(p);` then `p` everywhere |
| `cs/local-not-disposed` | warning | `new MemoryStream(...)`, an `HttpResponseMessage`, a `ServiceProvider`, a `Jint.Engine` created inline and never disposed, tests included. The script notes a stream or reader created inside a call | `using var` |
| `cs/equality-on-floats` | warning | `value != Math.Floor(value)`, `a == b` on `double`. The script refuses the `Math.Floor`/`Round`/`Truncate` form | `double.IsInteger(value)`, or a tolerance |
| `cs/empty-block` | warning | an empty `{ }` under `if`, `else`, `for`, `foreach`, `while`, `do`, `try`, `catch`, `finally` or `lock`, on one line (`catch { }`) or several (a drained `await foreach` whose header spans lines). An empty method, constructor or type body, an initializer and an empty `using` body pass | a comment inside the block saying why it is empty |
| `cs/useless-upcast` | warning | `(string?)null` inside a tuple or an argument | assert the two values apart: `Assert.Null(after)` |
| `cs/unused-collection` | error | a list or `JsonArray` that is filled and never read | delete it, with the lines that fill it |
| `cs/useless-assignment-to-local` | warning | `await using (var x = Start(...))` where `x` is never read; a local assigned and not used | `await using (Start(...))`; drop the local |
| `cs/call-to-object-tostring` | warning | `.ToString()` on a type that does not override it, as a test that a secret is not printed does. No pattern predicts it | `Convert.ToString((object)value, CultureInfo.InvariantCulture)` in such a test |
| `cs/xmldoc/missing-summary` | note | a public member documented with `<param>` only | a `<summary>` first |
| `cs/path-combine` | note | `Path.Combine(a, b)` | accepted in tests; in `src/` the VFS rule forbids physical paths anyway |
| `cs/linq/missed-select`, `cs/linq/missed-where`, `cs/coupled-types` | note | a `foreach` that maps or filters at once; a large facade | accepted when the loop throws or reads better; not a reason to restructure |

Read the alerts of a pull request with
`gh api "repos/Orkeon/orkeon/code-scanning/alerts?pr=<n>&state=open&per_page=100"`.

## Secret scan (gitleaks, `generic-api-key` and the provider rules)

- A literal next to a name that holds `key`, `secret`, `token` or `password` is flagged when it looks random:
  an invented environment-variable name (`ORKEON_TEST_451_NO_SUCH_KEY`), a fake identifier (`wrkspc_01Test`),
  a fake key (`sk-test-...`). Without the binary, the script notes a literal of 12 characters or more that
  mixes digits and letters; words joined by dashes (`unused-in-tests`) pass, as they pass gitleaks.
- Prefer a value already listed in `.gitleaks.toml`, or build the value at run time
  (`"sk-" + new string('a', 24)`).
- Otherwise add the exact value to the first allowlist of `.gitleaks.toml`, one line per value, never a path,
  in the same commit as the test, and document it in the comment block above the list: the value, the test
  that holds it, and why it is no credential. A real secret is never allowlisted: it is revoked.

## The rest of the list

- One `--no-incremental` Release build before the push: an incremental build hides CS0419 and analyzer
  errors of projects it did not rebuild (`--full` builds that way).
- A new public member is declared in the project's `PublicAPI.Unshipped.txt` (RS0016).
- A `Jint.Engine` built in a test sets `PromiseTimeout = HangGuards.EnginePromise` (an architecture test
  reads the test sources).
- A setting added or changed: regenerate the catalogue, the reference tables and the sample
  (`UPDATE_PRODUCED_FILES=1` on the `SettingsCatalogFile`, then `SettingsReferencePage|SettingsSampleFile`
  tests), and write its French sentence in `docs/fr/reference/configuration.settings.json`.
- A changed docs page is mirrored in `docs/fr/` in the same commit; `CHANGELOG.md` names the issue.
- A branch that adds an experimental id (`ORKEXP…`) checks it against `main` before it is rebased or
  re-applied: another branch may have taken the same number meanwhile.
- A fake-clock test advances the clock only once the timer it means is armed (wait on that timer), never
  on a count of pending timers: another timer may make up the count.
- After the push, read `gh pr checks <n>` until every check has ended: a pending check is not a green one.
