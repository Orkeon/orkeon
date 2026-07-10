# Example data policy

> **See also**: [Run your first example](../getting-started/run-your-first-example.md) · [Example README template](../templates/example-readme.md) · [VFS compliance](../architecture/vfs-compliance.md) · [Back to the index](../INDEX.md)

Most bundled examples ship their **crew definition** (`config.yaml`) and code, but
**not** the input data they operate on. This page explains why, and how to feed an
example your own data.

## The policy

- **Examples ship configuration, not datasets.** An example is a crew you can read
  and run — not a data distribution. When a README's run section says *"this
  example does not ship sample data yet"*, it means exactly that: no input files
  are committed alongside the `config.yaml`.
- **Curated examples that need a fixture carry a tiny one.** A handful of examples
  ship a small, synthetic sample so they run out of the box. Those have a
  **Required data** table in their README and a `--mount` in their run command;
  the two categories are easy to tell apart by that table.
- **No proprietary, copyrighted, or personal data** is ever committed to the
  repository.

### Why

- **Repository size** — realistic corpora (PDFs, datasets, scrapes) would bloat
  the clone for every user, most of whom only run a few examples.
- **Licensing** — third-party documents and datasets carry their own terms; we
  don't redistribute them.
- **Freshness** — many examples research live sources (news, prices, web pages).
  A snapshot committed today is stale tomorrow; letting the crew fetch current
  data is the point.
- **Reproducibility** — you control exactly what the crew sees, which makes runs
  auditable and results yours.

## How an example gets its data

Depending on the crew, one of three things is true:

1. **The tools fetch their own data.** Crews built around `web_scrape`,
   `http_api`, `search`, or similar tools pull live data at run time. You supply
   nothing — just an LLM profile and, usually, a topic via `--var` or
   `--initial-context`. See the flag reference in
   [Run your first example](../getting-started/run-your-first-example.md#every-flag-explained).
2. **You mount your own input.** Crews that read local files (PDF, CSV, JSON, a
   codebase) expect you to expose a host directory into the crew's
   [virtual file system](../architecture/vfs-compliance.md):

   ```bash
   dotnet run --project examples/runners/standard -- \
     --config examples/<path>/config.yaml \
     --settings examples/appsettings/appsettings.deepseek.local.json \
     --mount ./data:/data:ro \
     --mount ./out:/output:rw
   ```

   The crew's `config.yaml` refers to input by its **virtual** path (e.g.
   `/data/report.pdf`), never a host path. `:ro` for inputs, `:rw` for anything
   the crew writes.
3. **A committed sample fixture.** For curated examples, the fixture already sits
   in the example folder and the run command mounts it for you — nothing to
   provide.

### Where output goes

By convention crews write results to the `/output` mount. Map it to a local
directory with `--mount ./out:/output:rw`; declaring an `/output:rw` mount also
enables the automatic run-summary writer.

## Adding sample data to an example (contributors)

If your example genuinely needs a bundled fixture:

- **Keep it tiny and synthetic.** A few KB of hand-written or generated data, not
  a real-world dump.
- **Keep it license-clean.** No copyrighted or personal content. If it must
  resemble real data, generate it.
- **Place it in the example folder** and mount it read-only from the run command
  (`--mount ./data:/data:ro`).
- **Document it** in a **Required data** table in the example's README, following
  the [example README template](../templates/example-readme.md). That table (plus
  a `--mount` in the run command) is what moves the example out of the
  "does not ship sample data yet" category.
</content>
