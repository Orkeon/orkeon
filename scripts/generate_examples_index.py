#!/usr/bin/env python3
"""generate_examples_index.py — Generate examples/INDEX.md and examples/usecases.json
from the example catalog.

Scans the numbered example categories (``NN-name/`` with sub-folders ``NN-slug/``),
each carrying a crew definition (``config.yaml``, or a ``main.ork.ts`` TypeScript crew),
a use-case sheet (``usecase.yaml``) and usually a ``README.md``, and produces:

  * ``examples/INDEX.md``      — a navigable Markdown catalog;
  * ``examples/usecases.json`` — the use-case manifest (STUDIO-36): one entry per
    example, joining the hand-written fields of its ``usecase.yaml`` to the fields
    derived from its folder and its crew. The format of both is documented in
    examples/README.md ("Use-case sheet").

For every example it extracts:
  * category, folder number + slug
  * title      — first H1 of README.md, else the folder slug
  * process    — top-level ``process:`` of config.yaml
  * agents     — number of agents declared
  * tasks      — number of tasks declared
  * tools      — distinct tool names referenced in agents' ``tools:`` blocks
  * runner     — from the category README ``**Runner**:`` line (``standard`` default,
                 ``trading`` for 03-finance-trading)
  * data       — whether the folder ships a ``data/`` folder
  * readme     — whether a README.md is present

The manifest also derives ``requiresNetwork`` and ``requiresKeys`` from the tools,
through the explicit TOOL_NEEDS table below.

No third-party dependency: config.yaml is parsed with an indentation-aware mini-parser
tuned to the flat crew-config shape (process / agents.*.tools / tasks), and usecase.yaml
with a strict reader of the sheet's YAML subset. This keeps the generator runnable in CI
without PyYAML.

Usage:
  python3 scripts/generate_examples_index.py            # (re)write INDEX.md and usecases.json
  python3 scripts/generate_examples_index.py --check     # exit 1 if either is stale
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EXAMPLES = ROOT / "examples"
INDEX_NAME = "INDEX.md"
MANIFEST_NAME = "usecases.json"
USECASE_FILE = "usecase.yaml"

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
    crew: Path          # config.yaml, or main.ork.ts for a TypeScript crew
    fmt: str            # "yaml" | "ork.ts"
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


TS_HEADER_RE = re.compile(r"^//\s*orkeon-example:\s*(\{.*\})\s*$", re.MULTILINE)


def parse_ts_crew(ts: Path):
    """Metadata of a migrated TypeScript crew (EX-01): the one-line
    `// orkeon-example: {"process": ..., "agents": N, "tasks": N, "tools": [...]}`
    JSON header when present, else counts derived from the builder calls."""
    text = ts.read_text(encoding="utf-8", errors="replace")
    m = TS_HEADER_RE.search(text)
    if m:
        try:
            d = json.loads(m.group(1))
            return (d.get("process"), int(d.get("agents", 0)),
                    int(d.get("tasks", 0)), list(d.get("tools", [])))
        except (ValueError, TypeError):
            pass
    proc = re.search(r"\.process\(\s*\"([a-z]+)\"\s*\)", text)
    tool_blobs = " ".join(re.findall(r"\.tools\(\[(.*?)\]\)", text, re.DOTALL))
    tools = sorted(set(re.findall(r"\"([a-z0-9_]+)\"", tool_blobs)))
    return (proc.group(1) if proc else None,
            text.count("agentBuilder("), text.count("taskBuilder("), tools)


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
        ts = sub / "main.ork.ts"
        if not m or not (cfg.is_file() or ts.is_file()):
            continue
        number = int(m.group(1))
        slug = m.group(2)
        readme = sub / "README.md"
        process, agents, tasks, tools = (
            parse_config(cfg) if cfg.is_file() else parse_ts_crew(ts))
        cat.examples.append(Example(
            number=number,
            slug=slug,
            path=sub,
            crew=cfg if cfg.is_file() else ts,
            fmt="yaml" if cfg.is_file() else "ork.ts",
            title=read_h1(readme) or slug,
            process=process,
            agents=agents,
            tasks=tasks,
            tools=tools,
            has_readme=readme.is_file(),
            # The data/ folder alone: counting every file beside the crew made each
            # example "have data" the day it gained its usecase.yaml.
            has_data=(sub / "data").is_dir(),
        ))

    cat.examples.sort(key=lambda e: e.number)
    return cat


def collect_categories(examples: Path = EXAMPLES) -> list[Category]:
    cats: list[Category] = []
    for d in sorted(examples.iterdir()):
        if not d.is_dir() or d.name in EXCLUDED_TOP or not CATEGORY_RE.match(d.name):
            continue
        cats.append(collect_category(d))
    return cats


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
    a("Every numbered example runs on the stock `orkeon` CLI — YAML crews as "
      "`orkeon run <config.yaml>`, TypeScript crews (EX-01) as "
      "`orkeon run <main.ork.ts>` with their tools inside the script.")
    a("")
    a("Run any example with `bash examples/run-example.sh <category>/<example>` "
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
            link = f"[{e.title}]({c.dirname}/{e.path.name}/)"
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
    a("| [`raggable-tree/`](raggable-tree/) | C# programs + YAML crew | RaggableTree indexing: `basic-indexing`, `custom-adapter` (C#); `crew-yaml` (pure YAML, stock `orkeon` CLI). |")
    a("| [`scripting/`](scripting/) | `.ork.ts` scripts | TypeScript-syntax scripting DSL samples (hello-world → FSM/graph, `08-rag.ork.ts` for `rag.ingest`/`rag.query`). |")
    a("| [`cli-ts-commands/`](cli-ts-commands/) | `.cmd.ts` / `.ork.ts` | TypeScript CLI command examples. |")
    a("| [`local-embeddings/`](local-embeddings/) | C# program | On-device BGE-micro-v2 ONNX embeddings. |")
    a("| [`run-events/`](run-events/) | Python reader | Watching a run from another program: `orkeon run --events jsonl`, a ~90-line dependency-free reader, and a recorded stream to try it with no model configured. |")
    a("| [`service-host/`](service-host/) | JSON configuration | Hosting a crew as a daemon: one hosted crew, a Discord channel, mounts, and the two secrets named rather than written. |")
    a("| [`rag/`](rag/) | C# programs | RAG subsystem (fully offline, local BGE, no API key): `basic-ingestion` (incremental ingestion + cited queries), `hybrid-retrieval` (BM25 + RRF vs vector-only), `custom-reranker` (host-provided `IReranker` via `IRerankerRegistrar`), `crew-yaml` (crew `rag:`/`knowledge:` blocks); plus the `eval/` golden dataset. |")
    a("| [`09-experimental/llm-response-format/`](09-experimental/llm-response-format/) | `crew.yaml` + `.ork.ts` | Structured-output (`response_format`) demo. |")
    a("| [`09-experimental/streaming-demo/`](09-experimental/streaming-demo/) | C# program | Real-time streaming of agent execution (`IStreamingAgentExecutionService`). |")
    a("")

    return "\n".join(out) + "\n"


# The five languages of Studio's interface (STUDIO-37). French comes first: it is the
# reference text, and the other four are reviewed translations of it.
LANGUAGES = ("fr", "en", "es", "de", "zh-Hans")

# A sheet's keys, in the order every sheet writes them.
SHEET_KEYS = ("title", "problem", "tags", "mounts", "importable")
TEXT_KEYS = ("title", "problem")

# A tag is an English identifier, never displayed: lowercase words joined by hyphens.
TAG_RE = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")

# A mount in the team-relative spelling of Studio's TeamMountPaths: a folder inside the
# team (never "." or ".."), the virtual root it backs, ro or rw — "./data:/data:ro".
_SEGMENT = r"[A-Za-z0-9_][A-Za-z0-9._-]*"
MOUNT_RE = re.compile(
    rf"^\./(?P<folder>{_SEGMENT}(?:/{_SEGMENT})*)"
    rf":(?P<virtual>/{_SEGMENT}(?:/{_SEGMENT})*)"
    r":(?P<rights>ro|rw)$")

SheetError = tuple[int, str]    # (line, message) — line 0 is the file as a whole

_SHEET_KEY_RE = re.compile(r"^(?P<key>[A-Za-z][A-Za-z0-9-]*):(?P<rest>.*)$")
_SHEET_ITEM_RE = re.compile(r"^-(?P<rest>.*)$")
_TRAILER_RE = re.compile(r"^(?:\s+#.*)?\s*$")
_CONTROL_RE = re.compile(r"[\x00-\x1f\x7f]")
_JSON = json.JSONDecoder()


def _sheet_value(rest: str):
    """The value after ``key:`` or ``-``: one JSON value — a "double-quoted" string, a
    [list] or true/false, which YAML reads exactly as JSON does — then nothing but an
    optional ``# comment``. Raises ValueError."""
    if not rest.startswith(" "):
        raise ValueError("put a space before the value")
    text = rest.lstrip(" ")
    try:
        value, end = _JSON.raw_decode(text)
    except json.JSONDecodeError as exc:
        raise ValueError(
            f'expected a "double-quoted" string, a [list] or true/false ({exc.msg})') from None
    if not _TRAILER_RE.match(text[end:]):
        raise ValueError(f"unexpected text after the value: {text[end:].strip()!r}")
    return value


def parse_sheet(text: str) -> tuple[dict, dict, list[SheetError]]:
    """Read the YAML subset a use-case sheet is written in.

    A top-level line is ``key: value``, or ``key:`` over a block indented by exactly two
    spaces whose lines are all ``- value`` (a list) or all ``name: value`` (a mapping).
    Every value is single-line JSON (see _sheet_value); ``#`` comments and blank lines
    go anywhere. Anything else is an error rather than a guess, so a file this reads is
    a YAML file every YAML reader reads the same way.

    Returns (data, lines, errors); ``lines`` maps a key, a (key, name) or a
    (key, index) to its line number.
    """
    data: dict = {}
    lines: dict = {}
    errors: list[SheetError] = []
    block: str | None = None           # the top-level key whose block is open

    for lineno, raw in enumerate(text.splitlines(), start=1):
        body = raw.lstrip(" ").rstrip()
        if not body or body.startswith("#"):
            continue
        if body.startswith("\t"):
            errors.append((lineno, "indent with spaces, not tabs"))
            continue
        indent = len(raw) - len(raw.lstrip(" "))

        if indent == 0:
            block = None
            m = _SHEET_KEY_RE.match(body)
            if not m:
                errors.append((lineno, "expected `key:` or `key: value`"))
                continue
            key = m["key"]
            if key in data:
                errors.append((lineno, f"'{key}' is set twice"))
                continue
            lines[key] = lineno
            data[key] = None
            if _TRAILER_RE.match(m["rest"]):
                block = key            # its entries follow
                continue
            try:
                data[key] = _sheet_value(m["rest"])
            except ValueError as exc:
                errors.append((lineno, f"{key}: {exc}"))
            continue

        if block is None:
            errors.append((lineno, "an indented line goes under a `key:` written without a value"))
            continue
        if indent != 2:
            errors.append((lineno, f"{block}: indent its entries by exactly two spaces"))
            continue
        item = _SHEET_ITEM_RE.match(body)
        entry = None if item else _SHEET_KEY_RE.match(body)
        if item is None and entry is None:
            errors.append((lineno, f"{block}: expected `- value` or `name: value`"))
            continue
        current = data[block]
        if current is None:
            current = data[block] = [] if item else {}
        if isinstance(current, list) != bool(item):
            errors.append((lineno, f"{block}: mixes `- value` items and `name: value` entries"))
            continue
        try:
            if item:
                current.append(_sheet_value(item["rest"]))
                lines[(block, len(current) - 1)] = lineno
                continue
            name = entry["key"]
            if name in current:
                errors.append((lineno, f"{block}.{name} is set twice"))
                continue
            if _TRAILER_RE.match(entry["rest"]):
                errors.append((lineno, f"{block}.{name}: give it a value on the same line"))
                continue
            current[name] = _sheet_value(entry["rest"])
            lines[(block, name)] = lineno
        except ValueError as exc:
            errors.append((lineno, f"{block}: {exc}"))

    return data, lines, errors


def _check_texts(key: str, value, lines: dict) -> list[SheetError]:
    at = lines.get(key, 0)
    if not isinstance(value, dict):
        return [(at, f'{key}: expected one `lang: "text"` entry per language '
                     f"({', '.join(LANGUAGES)})")]
    errors: list[SheetError] = []
    for lang in value:
        if lang not in LANGUAGES:
            errors.append((lines.get((key, lang), at),
                           f"{key}: '{lang}' is not one of {', '.join(LANGUAGES)}"))
    for lang in LANGUAGES:
        if lang not in value:
            errors.append((at, f'{key}: \'{lang}\' is missing — write "" until the text exists'))
    written = [lang for lang in value if lang in LANGUAGES]
    if written != [lang for lang in LANGUAGES if lang in value]:
        errors.append((at, f"{key}: languages go in the order {', '.join(LANGUAGES)}"))
    for lang, text in value.items():
        where = lines.get((key, lang), at)
        if not isinstance(text, str):
            errors.append((where, f'{key}.{lang}: expected a "double-quoted" string'))
        elif _CONTROL_RE.search(text):
            errors.append((where, f"{key}.{lang}: one line of text — "
                                  "no line break, tab or control character"))
        elif text != text.strip():
            errors.append((where, f"{key}.{lang}: no leading or trailing spaces"))
    return errors


def _check_tags(value, lines: dict) -> list[SheetError]:
    at = lines.get("tags", 0)
    if not isinstance(value, list):
        return [(at, "tags: expected a list — [] when there is none")]
    errors: list[SheetError] = []
    seen: set[str] = set()
    for i, tag in enumerate(value):
        where = lines.get(("tags", i), at)
        if not isinstance(tag, str) or not TAG_RE.match(tag):
            errors.append((where, f"tags: {tag!r} is not a lowercase English identifier "
                                  'such as "market-analysis"'))
        elif tag in seen:
            errors.append((where, f"tags: '{tag}' is listed twice"))
        else:
            seen.add(tag)
    return errors


def _check_mounts(value, lines: dict) -> list[SheetError]:
    at = lines.get("mounts", 0)
    if not isinstance(value, list):
        return [(at, "mounts: expected a list — [] when the example needs none")]
    errors: list[SheetError] = []
    roots: set[str] = set()
    for i, mount in enumerate(value):
        where = lines.get(("mounts", i), at)
        m = MOUNT_RE.match(mount) if isinstance(mount, str) else None
        if m is None:
            errors.append((where, f"mounts: {mount!r} is not a team-relative mount "
                                  '"./<folder>:<virtual root>:ro|rw" such as "./data:/data:ro"'))
        elif m["virtual"] in roots:
            errors.append((where, f"mounts: {m['virtual']} is mounted twice"))
        else:
            roots.add(m["virtual"])
    return errors


def validate_sheet(data: dict, lines: dict) -> list[SheetError]:
    """The sheet's schema: SHEET_KEYS in that order; ``title`` and ``problem`` give each
    of LANGUAGES, in that order, one line of text (empty until it is written); ``tags``
    lists distinct TAG_RE identifiers; ``mounts`` lists MOUNT_RE entries, one per virtual
    root; ``importable`` is true or false."""
    errors: list[SheetError] = []
    for key in data:
        if key not in SHEET_KEYS:
            errors.append((lines[key], f"unknown key '{key}' — a sheet holds "
                                       f"{', '.join(SHEET_KEYS)}"))
    for key in SHEET_KEYS:
        if key not in data:
            errors.append((0, f"'{key}' is missing"))
    if [key for key in data if key in SHEET_KEYS] != [key for key in SHEET_KEYS if key in data]:
        errors.append((0, f"keys go in the order {', '.join(SHEET_KEYS)}"))

    for key in TEXT_KEYS:
        if key in data:
            errors.extend(_check_texts(key, data[key], lines))
    if "tags" in data:
        errors.extend(_check_tags(data["tags"], lines))
    if "mounts" in data:
        errors.extend(_check_mounts(data["mounts"], lines))
    if "importable" in data and not isinstance(data["importable"], bool):
        errors.append((lines["importable"], "importable: expected true or false"))
    return errors


def load_sheet(path: Path) -> tuple[dict | None, list[SheetError]]:
    """The validated sheet at ``path``, or None and the reasons it is not one."""
    if not path.is_file():
        return None, [(0, "missing — every numbered example carries one (see examples/README.md)")]
    try:
        text = path.read_text(encoding="utf-8-sig")
    except UnicodeDecodeError as exc:
        return None, [(0, f"not UTF-8 ({exc.reason})")]
    data, lines, errors = parse_sheet(text)
    if not errors:
        errors = validate_sheet(data, lines)
    return (None if errors else data), errors


@dataclass(frozen=True)
class ToolNeeds:
    """What a tool needs beyond the LLM provider and the run's own mounts."""
    network: bool = False           # it reaches the internet itself
    keys: tuple[str, ...] = ()      # third-party keys, by the variable the runtime reads


# web_search's Tavily key: WebSearchTool asks the secret chain for TAVILY_API_KEY, which
# `orkeon run` reads from ORKEON_TAVILY_API_KEY (Studio.Core's ToolCatalog.TavilyKeyEnv).
TAVILY_KEY_ENV = "ORKEON_TAVILY_API_KEY"

# The tools of the finance crews' shared TypeScript module (examples/03-finance-trading/
# _tools/): deterministic mocks seeded by the symbol — "no disk, no network", as the
# header of each of its files says.
FINANCE_MODULE_TOOLS = (
    "alert_management", "alternative_data", "arima_prediction", "audit_trail",
    "backtesting", "black_litterman", "compliance_check", "correlation_analysis",
    "cvar_calculation", "dashboard_metrics", "ensemble_prediction", "factor_exposure",
    "fundamental_data", "hierarchical_risk_parity", "market_regime_classification",
    "mean_variance_optimization", "pattern_recognition", "portfolio_rebalancing",
    "prophet_prediction", "regulatory_reporting", "risk_parity", "smart_order_routing",
    "stress_testing", "technical_indicators", "twap_execution", "var_calculation",
    "vwap_execution",
)

# Every tool a numbered example uses, classified from its implementation. An example's
# requiresNetwork is true when one of its tools reaches the internet, and requiresKeys
# lists the third-party keys its tools read — the LLM provider's own key never counts.
# A tool missing here stops the generator: a new tool gets classified, never defaulted.
TOOL_NEEDS: dict[str, ToolNeeds] = {
    # src/tools/Orkeon.Tools.Web — each one connects to hosts on the internet: HttpApiTool
    # and WebScrapeTool to the URL they are given, GitHubTool to api.github.com (`orkeon
    # run` registers it without a token, so there is no key to set), WebSearchTool to the
    # Tavily Search API.
    "http_api": ToolNeeds(network=True),
    "web_scrape": ToolNeeds(network=True),
    "github": ToolNeeds(network=True),
    "web_search": ToolNeeds(network=True, keys=(TAVILY_KEY_ENV,)),
    # Offline: the run's mounts, its memory, a local process or the console.
    **dict.fromkeys((
        "directory_read", "email_parser", "file_read", "file_write",   # Orkeon.Tools.FileSystem
        "csv_reader", "json_tool", "pdf_reader", "xml_parser",         # Orkeon.Tools.Data
        # Orkeon.Tools.Data: the connection comes in the call and names a database, not
        # a web service — and no example ships or names one.
        "relational_database_query",
        "semantic_search", "human_input",   # Orkeon.Infrastructure: memory search, stdin
        "shell_command",                    # Orkeon.Tools.Code: a local process, allowlisted
    ), ToolNeeds()),
    **dict.fromkeys(FINANCE_MODULE_TOOLS, ToolNeeds()),
}


def derive_needs(tools: list[str]) -> tuple[bool, list[str], list[str]]:
    """(requiresNetwork, requiresKeys, the tools TOOL_NEEDS does not classify)."""
    known = [TOOL_NEEDS[t] for t in tools if t in TOOL_NEEDS]
    return (any(needs.network for needs in known),
            sorted({key for needs in known for key in needs.keys}),
            [t for t in tools if t not in TOOL_NEEDS])


Problem = tuple[Path, int, str]     # (file, line — 0 for the whole file, message)

MANIFEST_COMMENT = (
    "GENERATED FILE — DO NOT EDIT BY HAND. Built by scripts/generate_examples_index.py "
    "from each numbered example's crew and usecase.yaml; regenerate with: "
    "bash scripts/generate-examples-index.sh")


def build_manifest(cats: list[Category]) -> tuple[dict, list[Problem]]:
    """The manifest of every example whose sheet is valid, and the problems keeping the
    others out: a missing or invalid sheet, an id another example already has, a tool
    TOOL_NEEDS does not classify. A manifest built with problems is incomplete, and is
    never written."""
    problems: list[Problem] = []
    entries: list[dict] = []
    owners: dict[str, str] = {}
    for c in cats:
        for e in c.examples:
            where = f"{c.dirname}/{e.path.name}"
            owner = owners.setdefault(e.path.name, where)
            if owner != where:
                problems.append((e.path, 0, f"id '{e.path.name}' is already the id of {owner} — "
                                            "an example's folder name is its id, "
                                            "unique across categories"))
            sheet, errors = load_sheet(e.path / USECASE_FILE)
            problems.extend((e.path / USECASE_FILE, line, msg) for line, msg in errors)
            network, keys, unknown = derive_needs(e.tools)
            problems.extend((e.crew, 0, f"tool '{tool}' is not classified — add it to "
                                        "TOOL_NEEDS in scripts/generate_examples_index.py")
                            for tool in unknown)
            if sheet is None:
                continue
            entries.append({
                "id": e.path.name,
                "category": c.dirname,
                "number": e.number,
                "format": e.fmt,
                "process": e.process,
                "agents": e.agents,
                "tasks": e.tasks,
                "tools": sorted(e.tools),
                "hasSampleData": e.has_data,
                "requiresNetwork": network,
                "requiresKeys": keys,
                "title": sheet["title"],
                "problem": sheet["problem"],
                "tags": sheet["tags"],
                "mounts": sheet["mounts"],
                "importable": sheet["importable"],
            })
    entries.sort(key=lambda u: (u["category"], u["number"], u["id"]))
    manifest = {"$comment": MANIFEST_COMMENT, "languages": list(LANGUAGES), "useCases": entries}
    return manifest, problems


def render_manifest(manifest: dict) -> str:
    """Deterministic JSON: sorted keys, two-space indent, UTF-8 kept as is, final newline."""
    return json.dumps(manifest, ensure_ascii=False, indent=2, sort_keys=True) + "\n"


def _shown(path: Path) -> str:
    try:
        return path.relative_to(ROOT).as_posix()
    except ValueError:
        return str(path)


def _read(path: Path) -> str | None:
    return path.read_text(encoding="utf-8") if path.is_file() else None


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description="Generate examples/INDEX.md and examples/usecases.json.")
    ap.add_argument("--check", action="store_true",
                    help="write nothing; exit 1 if a file is stale or cannot be generated")
    ap.add_argument("--root", type=Path, default=EXAMPLES,
                    help="the examples tree to scan and write into (defaults to ./examples)")
    args = ap.parse_args(argv[1:])

    examples = args.root.resolve()
    index_path = examples / INDEX_NAME
    manifest_path = examples / MANIFEST_NAME
    cats = collect_categories(examples)
    index = render(cats)
    manifest, problems = build_manifest(cats)
    for path, line, msg in problems:
        print(f"error: {_shown(path)}{f':{line}' if line else ''}: {msg}", file=sys.stderr)

    if args.check:
        stale = [index_path] if _read(index_path) != index else []
        if not problems and _read(manifest_path) != render_manifest(manifest):
            stale.append(manifest_path)
        for path in stale:
            print(f"{_shown(path)} is stale — run scripts/generate-examples-index.sh",
                  file=sys.stderr)
        if problems:
            print(f"{_shown(manifest_path)} cannot be generated: {len(problems)} problem(s), "
                  "listed above and by scripts/lint-example-configs.py", file=sys.stderr)
        if stale or problems:
            return 1
        print(f"{_shown(index_path)} and {_shown(manifest_path)} are up to date.")
        return 0

    index_path.write_text(index, encoding="utf-8", newline="\n")
    if problems:
        print(f"Wrote {_shown(index_path)}; {_shown(manifest_path)} NOT written: "
              f"{len(problems)} problem(s), listed above", file=sys.stderr)
        return 1
    manifest_path.write_text(render_manifest(manifest), encoding="utf-8", newline="\n")
    total = sum(len(c.examples) for c in cats)
    print(f"Wrote {_shown(index_path)} and {_shown(manifest_path)} — "
          f"{total} examples across {len(cats)} categories.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
