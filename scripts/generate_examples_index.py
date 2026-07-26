#!/usr/bin/env python3
"""generate_examples_index.py — Generate examples/INDEX.md from the example catalog.

Scans the numbered example categories (``NN-name/`` with sub-folders ``NN-slug/``),
each carrying a ``config.yaml`` (crew definition) and usually a ``README.md``, and
produces a navigable Markdown catalog.

For every example it extracts:
  * category, folder number + slug
  * title      — first H1 of README.md, else the folder slug
  * process    — top-level ``process:`` of config.yaml
  * agents     — number of agents declared
  * tasks      — number of tasks declared
  * tools      — distinct tool names referenced in agents' ``tools:`` blocks
  * runner     — from the category README ``**Runner**:`` line (``standard`` default,
                 ``trading`` for 03-finance-trading)
  * data       — whether the folder ships extra data files beyond config/README
  * readme     — whether a README.md is present

No third-party dependency: config.yaml is parsed with an indentation-aware mini-parser
tuned to the flat crew-config shape (process / agents.*.tools / tasks). This keeps the
generator runnable in CI without PyYAML.

Usage:
  python3 scripts/generate_examples_index.py            # (re)write examples/INDEX.md
  python3 scripts/generate_examples_index.py --check     # exit 1 if INDEX.md is stale
"""
from __future__ import annotations

import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EXAMPLES = ROOT / "examples"
INDEX = EXAMPLES / "INDEX.md"

# Folders that are NOT part of the numbered catalog scan.
EXCLUDED_TOP = {
    "_shared", "_legacy", "others", "runners", "test-reports", ".vs",
    "bin", "obj", "obj-linux",
}

CATEGORY_RE = re.compile(r"^\d{2}-")           # 01-enterprise, 02-science-research, …
EXAMPLE_RE = re.compile(r"^(\d+)-(.+)$")        # 01-research-assistant → (1, research-assistant)
H1_RE = re.compile(r"^#\s+(.+?)\s*$")
RUNNER_RE = re.compile(r"\*\*Runner\*\*:\s*`([^`]+)`")


@dataclass
class Example:
    number: int
    slug: str
    path: Path
    title: str
    process: str
    agents: int
    tasks: int
    tools: list[str]
    has_readme: bool
    has_data: bool


@dataclass
class Category:
    dirname: str
    title: str
    runner: str
    examples: list[Example] = field(default_factory=list)


def read_h1(readme: Path) -> str | None:
    if not readme.is_file():
        return None
    for line in readme.read_text(encoding="utf-8", errors="replace").splitlines():
        m = H1_RE.match(line)
        if m:
            return m.group(1).strip()
    return None


def read_runner(readme: Path) -> str:
    if readme.is_file():
        m = RUNNER_RE.search(readme.read_text(encoding="utf-8", errors="replace"))
        if m:
            return m.group(1).strip()
    return "standard"


def _indent(line: str) -> int:
    return len(line) - len(line.lstrip(" "))


def parse_config(cfg: Path) -> tuple[str, int, int, list[str]]:
    """Return (process, agent_count, task_count, distinct_tools).

    Indentation-aware scan of the crew config. Tool names are collected only from
    list items directly under an agent's ``tools:`` key, never from task
    ``dependencies:``/``context:`` lists (which reference other task names).
    """
    process = "unknown"
    agent_count = 0
    task_count = 0
    tools: list[str] = []
    seen: set[str] = set()

    lines = cfg.read_text(encoding="utf-8", errors="replace").splitlines()

    section: str | None = None          # "agents" | "tasks" | None
    section_child_indent: int | None = None
    in_tools = False
    tools_indent = -1

    for raw in lines:
        # Skip blank and comment-only lines.
        stripped = raw.strip()
        if not stripped or stripped.startswith("#"):
            continue
        indent = _indent(raw)

        # Top-level keys (indent 0) reset the current section.
        if indent == 0:
            in_tools = False
            m = re.match(r"^process:\s*['\"]?([A-Za-z_]+)", raw)
            if m:
                process = m.group(1).lower()
                section = None
                continue
            if re.match(r"^agents:\s*$", raw):
                section = "agents"
                section_child_indent = None
                continue
            if re.match(r"^tasks:\s*$", raw):
                section = "tasks"
                section_child_indent = None
                continue
            section = None
            continue

        if section in ("agents", "tasks"):
            # First indented child under the section fixes the "entry" indent level.
            if section_child_indent is None:
                section_child_indent = indent
            # A key at exactly the entry indent = a new agent/task entry.
            if indent == section_child_indent and re.match(r"^[A-Za-z0-9_]+:", stripped):
                in_tools = False
                if section == "agents":
                    agent_count += 1
                else:
                    task_count += 1
                continue

            if section == "agents":
                # Detect a tools: key deeper than the agent entry.
                mt = re.match(r"^tools:\s*(\[\s*\])?\s*$", stripped)
                if mt and indent > section_child_indent:
                    if mt.group(1):          # inline "tools: []" → empty
                        in_tools = False
                    else:
                        in_tools = True
                        tools_indent = indent
                    continue
                if in_tools:
                    if indent > tools_indent and stripped.startswith("- "):
                        name = stripped[2:].strip().strip("'\"")
                        if name and name not in seen:
                            seen.add(name)
                            tools.append(name)
                        continue
                    # Any line not deeper than tools: closes the block.
                    if indent <= tools_indent:
                        in_tools = False

    return process, agent_count, task_count, tools


def collect_category(cat_dir: Path) -> Category:
    runner = read_runner(cat_dir / "README.md")
    title = read_h1(cat_dir / "README.md") or cat_dir.name
    cat = Category(dirname=cat_dir.name, title=title, runner=runner)

    for sub in sorted(cat_dir.iterdir()):
        if not sub.is_dir():
            continue
        m = EXAMPLE_RE.match(sub.name)
        cfg = sub / "config.yaml"
        if not m or not cfg.is_file():
            continue
        number = int(m.group(1))
        slug = m.group(2)
        readme = sub / "README.md"
        process, agents, tasks, tools = parse_config(cfg)
        extras = [
            p for p in sub.iterdir()
            if p.name not in ("config.yaml", "README.md")
            and p.name not in ("bin", "obj", "obj-linux")
        ]
        cat.examples.append(Example(
            number=number,
            slug=slug,
            path=sub,
            title=read_h1(readme) or slug,
            process=process,
            agents=agents,
            tasks=tasks,
            tools=tools,
            has_readme=readme.is_file(),
            has_data=bool(extras),
        ))

    cat.examples.sort(key=lambda e: e.number)
    return cat


def collect_categories() -> list[Category]:
    cats: list[Category] = []
    for d in sorted(EXAMPLES.iterdir()):
        if not d.is_dir() or d.name in EXCLUDED_TOP or not CATEGORY_RE.match(d.name):
            continue
        cats.append(collect_category(d))
    return cats


def rel(path: Path) -> str:
    return path.relative_to(EXAMPLES).as_posix()


def fmt_process(p: str) -> str:
    return p.capitalize() if p != "unknown" else "—"


def fmt_tools(tools: list[str]) -> str:
    if not tools:
        return "—"
    return ", ".join(f"`{t}`" for t in tools)


def render(cats: list[Category]) -> str:
    total = sum(len(c.examples) for c in cats)
    out: list[str] = []
    a = out.append

    a("<!-- GENERATED FILE — DO NOT EDIT BY HAND. -->")
    a("<!-- Regenerate with: bash scripts/generate-examples-index.sh -->")
    a("")
    a("# Orkeon Examples — Catalog")
    a("")
    a(f"Auto-generated index of the **{total}** numbered crew examples shipped under "
      "`examples/`, grouped by the nine thematic categories. Each row links to the "
      "example folder and summarizes its crew configuration (process, agent/task "
      "counts, referenced tools).")
    a("")
    a("> This file is generated by `scripts/generate_examples_index.py`. "
      "Do not edit it by hand — run `bash scripts/generate-examples-index.sh` to refresh it.")
    a("")

    # Runners note.
    a("## Runners")
    a("")
    a("Numbered examples are executed through a runner project in `examples/runners/`:")
    a("")
    a("| Runner | Purpose |")
    a("|--------|---------|")
    a("| `standard` | Default runner for all categories except finance/trading. |")
    a("| `trading` | Adds 44 specialized trading tools; used by **03 - Finance & Trading**. |")
    a("| `interactive` | Human-in-the-loop console runners (e.g. `interactive-claim-verification`, `interactive-interview-spec-forge`) for the interactive examples. |")
    a("")
    a("Run any example with `bash examples/run-example.sh <runner> <category>/<example>` "
      "(see `examples/README.md`).")
    a("")

    # Category summary.
    a("## Categories")
    a("")
    a("| Category | Examples | Runner |")
    a("|----------|:--------:|--------|")
    for c in cats:
        anchor = c.title.lower()
        anchor = re.sub(r"[^a-z0-9 -]", "", anchor).strip().replace(" ", "-")
        a(f"| [{c.title}](#{anchor}) | {len(c.examples)} | `{c.runner}` |")
    a("")

    # Per-category tables.
    for c in cats:
        a(f"## {c.title}")
        a("")
        a(f"Runner: `{c.runner}` · {len(c.examples)} examples · "
          f"folder [`{c.dirname}/`]({c.dirname}/)")
        a("")
        a("| # | Example | Process | Agents | Tasks | Tools | README |")
        a("|---|---------|---------|:------:|:-----:|-------|:------:|")
        for e in c.examples:
            link = f"[{e.title}]({rel(e.path)}/)"
            readme_cell = "✅" if e.has_readme else "—"
            a(f"| {e.number} | {link} | {fmt_process(e.process)} | "
              f"{e.agents} | {e.tasks} | {fmt_tools(e.tools)} | {readme_cell} |")
        a("")

    # Non-numbered / code-based examples.
    a("## Other examples (not in the numbered catalog)")
    a("")
    a("These live under `examples/` but are code- or script-driven rather than "
      "pure `config.yaml` crews, so they are not counted in the totals above:")
    a("")
    a("| Folder | Kind | Notes |")
    a("|--------|------|-------|")
    a("| [`raggable-tree/`](raggable-tree/) | C# programs | RaggableTree indexing: `basic-indexing`, `crew-yaml`, `custom-adapter`. |")
    a("| [`scripting/`](scripting/) | `.ork.ts` scripts | TypeScript-syntax scripting DSL samples (hello-world → FSM/graph, `08-rag.ork.ts` for `rag.ingest`/`rag.query`). |")
    a("| [`cli-ts-commands/`](cli-ts-commands/) | `.cmd.ts` / `.ork.ts` | TypeScript CLI command examples. |")
    a("| [`local-embeddings/`](local-embeddings/) | C# program | On-device BGE-micro-v2 ONNX embeddings. |")
    a("| [`rag/`](rag/) | C# programs | RAG subsystem (fully offline, local BGE, no API key): `basic-ingestion` (incremental ingestion + cited queries), `hybrid-retrieval` (BM25 + RRF vs vector-only), `custom-reranker` (host-provided `IReranker` via `IRerankerRegistrar`), `crew-yaml` (crew `rag:`/`knowledge:` blocks); plus the `eval/` golden dataset. |")
    a("| [`09-experimental/llm-response-format/`](09-experimental/llm-response-format/) | `crew.yaml` + `.ork.ts` | Structured-output (`response_format`) demo. |")
    a("")

    return "\n".join(out) + "\n"


def main(argv: list[str]) -> int:
    check = "--check" in argv[1:]
    cats = collect_categories()
    content = render(cats)

    if check:
        current = INDEX.read_text(encoding="utf-8") if INDEX.is_file() else ""
        if current != content:
            print("examples/INDEX.md is stale — run scripts/generate-examples-index.sh",
                  file=sys.stderr)
            return 1
        print("examples/INDEX.md is up to date.")
        return 0

    INDEX.write_text(content, encoding="utf-8")
    total = sum(len(c.examples) for c in cats)
    print(f"Wrote {INDEX.relative_to(ROOT)} — {total} examples across {len(cats)} categories.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
