> 🇫🇷 [Version française](../fr/templates/example-readme.md)

# Example README template

> A gabarit for `examples/**/README.md`. Copy the block below into a new example's
> `README.md` and fill each section. Delete this preamble and any optional section
> that does not apply. Keep it short — the point is to let someone run the example
> without reading the code.
>
> **Required sections**: What it does · Prerequisites · Required data · Run it ·
> Expected output · Approx. duration & cost.

---

# <Example title>

> One or two sentences: what this crew produces and why it is interesting.

## What it does

- **Process**: `Sequential` | `Hierarchical` | `Parallel` | `Consensual` | `Graph` | `Autonomous`
- **Agents**: <n> — brief role list
- **Tools**: `tool_a`, `tool_b`, …
- **Key features**: what this example demonstrates (task dependencies, memory, A2A, …)
- **Runner**: `orkeon` CLI (default) | `orkeon-trading` (finance) | `orkeon-interactive` | …

## Prerequisites

- .NET SDK ≥ 10.0.300 (source) — or the .NET 10 runtime (release binary)
- An LLM profile (see the profile matrix under `examples/appsettings/`); this example was
  validated against `<provider>`.
- <Any other prerequisite: a running service, an API key beyond the LLM, …>

## Required data

If the crew reads input files, list them so the reader knows what to mount.
Delete this section if the example needs no input data.

| Virtual path | Mount flag | Purpose |
|---|---|---|
| `/data/<file>` | `--mount ./data:/data:ro` | <what it contains> |

## Run it

Give the **exact**, copy-pasteable command for each supported way. The `orkeon`
CLI is the default entry point (`orkeon run <config>`); the always-required
"from source" row plus, for showcase examples, a binary and a container row.

**With the `orkeon` CLI** (installed release binary or `dotnet tool install`):

```bash
orkeon run examples/<path>/config.yaml \
  --settings path/to/appsettings.local.json \
  --mount ./out:/output:rw
```

**From a source checkout** (no install — runs your local code):

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run \
  examples/<path>/config.yaml \
  --settings examples/appsettings/appsettings.<provider>.local.json \
  --mount ./out:/output:rw
```

**From the container** (showcase examples only — entry point is `orkeon`):

```bash
docker run --rm \
  -v "$PWD/out:/output" \
  -v "$PWD/appsettings.local.json:/app/appsettings.local.json:ro" \
  ghcr.io/orkeon/orkeon-runners \
  run examples/<path>/config.yaml \
  --settings /app/appsettings.local.json \
  --mount /output:/output:rw
```

> **Finance / trading example?** Swap the CLI for `orkeon-trading --config
> examples/<path>/config.yaml …` (it adds the 44 trading tools). In the
> container, select it with `-e ORKEON_RUNNER=trading`.
> Flag reference: [Run your first example](../getting-started/run-your-first-example.md#every-flag-explained)
> (adjust the relative path once this file lives in an example folder).

## Expected output

Describe what a successful run prints and/or writes. If the crew writes a file to
`/output`, name it and describe its shape (e.g. "a Markdown report with an
executive summary, thematic sections, and a bibliography").

## Approx. duration & cost

- **Duration**: ~<n> min on <provider/model>
- **Cost**: ~<n> LLM calls; rough token / \$ estimate if known (say "local model —
  no API cost" when applicable)
</content>
