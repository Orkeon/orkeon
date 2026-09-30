> 🇫🇷 [Version française](../fr/templates/example-readme.md)

# Example README template

> A template for `examples/**/README.md`. Copy the block below into a new example's
> `README.md` and fill each section. Delete this preamble and any optional section
> that does not apply. Keep it short — the point is to let someone run the example
> without reading the code.
>
> **Sections**: What it does · Prerequisites · Required data · Run it ·
> Expected output · Approx. duration & cost. The ten showcase examples follow this
> layout; the others keep their own sections around a **Run it** block that
> `scripts/add-readme-run-section.py` generates between `BEGIN run-it` / `END run-it`
> comments (edit the script, not the block — the showcases and `16-interactive-qa`
> are curated by hand and skipped).
>
> **What CI checks** (`scripts/lint-example-readmes.py`, over every numbered example
> that has a `config.yaml` or a `main.ork.ts`): the README exists; a launch heading
> (`## Run`, `## Run it`, `## Running`, `## Usage`, `## Lancer`, `## Exécuter` — any level
> from `##` to `######`) holds an `orkeon run <config>` command, or the from-source
> `dotnet run --project …Scripting.Cli -- run <config>`, whose `.yaml`/`.ork.ts` path —
> written from the repository root — exists (a command outside such a heading is only a
> warning); every relative link resolves; the category README mentions the example; and
> `examples/INDEX.md` / `examples/usecases.json` are fresh. The section list above is
> editorial — not enforced.
>
> **Beside the README**: a numbered example also carries a `usecase.yaml` — its title
> and problem statement in five languages, its search tags, the mounts it needs and
> whether it can be imported as a team. Copy a sibling's and follow
> [the use-case sheet format](https://github.com/Orkeon/orkeon/blob/main/examples/README.md#use-case-sheet-usecaseyaml);
> CI (`scripts/lint-example-configs.py`) fails without it, or when the sheet disagrees
> with the crew (a crew that writes files needs a `:rw` mount, a `./data` mount needs a
> `data/` folder).

> NB: example READMEs are written in English (they live outside `docs/`, so the bilingual parity contract does not apply to them) — this template's French twin exists for reference only.

---

# `<Example title>`

> One or two sentences: what this crew produces and why it is interesting.

## What it does

- **Process**: `Sequential` | `Hierarchical` | `Parallel` | `Consensual` | `Graph` | `Autonomous`
- **Agents**: `<n>` — brief role list
- **Tools**: `tool_a`, `tool_b`, …
- **Key features**: what this example demonstrates (task dependencies, memory, A2A, …)
- **Runner**: `orkeon` CLI (say "TypeScript crew" when the crew is a `main.ork.ts`)

## Prerequisites

- .NET SDK ≥ 10.0.300 (source) — or the .NET 10 runtime (release binary)
- An LLM profile (see the profile matrix under `examples/appsettings/`); this example was
  validated against `<provider>`.
- `<Any other prerequisite: a running service, an API key beyond the LLM, …>`

## Required data

If the crew reads input files, list them so the reader knows what to mount.
If it needs none, say so in one line (the generated run block says "this example
does not ship sample data yet" and links the
[example data policy](../reference/example-data-policy.md)).

| Virtual path | Mount flag | Purpose |
|---|---|---|
| `/data/<file>` | `--mount ./data:/data:ro` | `<what it contains>` |

List the same mounts under `mounts:` in the example's `usecase.yaml`, relative to
the team folder (`./data:/data:ro`, `./output:/output:rw`).

## Run it

Give the **exact**, copy-pasteable command, with the crew path written from the
repository root (`examples/<path>/config.yaml`, or `examples/<path>/main.ork.ts` for a
TypeScript crew). The `orkeon` CLI is the default entry point (`orkeon run <config>`);
mention the from-source form beside it, and add a container row when it helps. Several
mounts go space-separated after **one** `--mount`.

**With the `orkeon` CLI** (installed release binary or `dotnet tool install`):

```bash
orkeon run examples/<path>/config.yaml \
  --settings examples/appsettings/appsettings.<provider>.local.json \
  --mount ./out:/output:rw
```

To only confirm the crew loads and its mounts are accepted (no LLM call), append
`--validate`.

**From a source checkout** (no install — runs your local code):

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run \
  examples/<path>/config.yaml \
  --settings examples/appsettings/appsettings.<provider>.local.json \
  --mount ./out:/output:rw
```

**From the container** (optional — entry point is `orkeon`; the bundled examples live
under `/app/examples`):

```bash
docker run --rm \
  -v "$PWD/out:/output" \
  -v "$PWD/appsettings.local.json:/app/appsettings.local.json:ro" \
  ghcr.io/orkeon/orkeon-runners \
  run examples/<path>/config.yaml \
  --settings /app/appsettings.local.json \
  --mount /output:/output:rw
```

> Flag reference: [Run your first example](../getting-started/run-your-first-example.md#every-flag-explained)
> (adjust the relative path once this file lives in an example folder).

## Expected output

Describe what a successful run prints and/or writes. If the crew writes a file to
`/output`, name it and describe its shape (e.g. "a Markdown report with an
executive summary, thematic sections, and a bibliography").

## Approx. duration & cost

- **Duration**: ~`<n>` min on `<provider/model>`
- **Cost**: ~`<n>` LLM calls; rough token / \$ estimate if known (say "local model —
  no API cost" when applicable)
