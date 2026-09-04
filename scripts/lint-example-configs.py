#!/usr/bin/env python3
"""lint-example-configs.py — Static lint for the numbered example ``config.yaml`` crews.

Scans every ``examples/NN-category/NN-slug/config.yaml`` and checks:

  (a) TOOL NAMES  — every name in an agent's ``tools:`` block must exist in the runtime
      tool manifest for that example's runner. Unknown names are ERRORS, with a
      closest-match suggestion (difflib). The runner is ``trading`` for the
      ``03-finance-trading`` category (allowed set = standard ∪ trading) and
      ``standard`` for every other category.

  (b) DATA PATHS  — relative file/dir paths ending in a data extension
      (.csv/.json/.txt/.pdf/.xlsx/.docx/.xml/.md) that are mentioned in task
      descriptions/expected-outputs and look like they point at the example's own
      ``data/`` folder must exist on disk. Missing ones are WARNINGS only (not every
      example ships fixture data yet).

  (c) STRUCTURE   — the YAML parses and the top-level ``process``, ``agents`` and
      ``tasks`` sections are present and non-empty.

Exit status: non-zero if any ERROR is found; warnings never fail the build.

No third-party dependency: config.yaml is parsed with the same indentation-aware
mini-parser used by generate_examples_index.py, so this runs in CI without PyYAML.

Usage:
  python3 scripts/lint-example-configs.py
  python3 scripts/lint-example-configs.py --manifest path/to/standard.txt \
                                          --trading-manifest path/to/trading.txt
  python3 scripts/lint-example-configs.py --list-tools   # regenerate manifests from runners
"""
from __future__ import annotations

import argparse
import difflib
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EXAMPLES = ROOT / "examples"
DATA_DIR = Path(__file__).resolve().parent / "data"

DEFAULT_STANDARD_MANIFEST = DATA_DIR / "tool-manifest-standard.txt"
DEFAULT_TRADING_MANIFEST = DATA_DIR / "tool-manifest-trading.txt"

# Category whose examples run on the trading runner (standard ∪ trading tools).
TRADING_CATEGORY = "03-finance-trading"

CATEGORY_RE = re.compile(r"^\d{2}-")
EXAMPLE_RE = re.compile(r"^(\d+)-(.+)$")

# A relative path token ending in a data-file extension, e.g. data/prices.csv
DATA_EXT = ("csv", "json", "txt", "pdf", "xlsx", "docx", "xml", "md")
DATA_PATH_RE = re.compile(
    r"(?<![\w./-])((?:[\w-]+/)*[\w. -]+?\.(?:" + "|".join(DATA_EXT) + r"))(?![\w])"
)


# ── manifest loading ────────────────────────────────────────────────────────
def load_manifest(path: Path) -> set[str]:
    if not path.is_file():
        sys.exit(f"error: manifest not found: {path}")
    names: set[str] = set()
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            names.add(line)
    return names


def manifest_from_list_tools(runner: str) -> set[str] | None:
    """Best-effort invocation of a runner's ``--list-tools`` contract."""
    proj = EXAMPLES / "runners" / runner
    if not proj.is_dir():
        return None
    try:
        out = subprocess.run(
            ["dotnet", "run", "--project", str(proj), "--", "--list-tools"],
            capture_output=True, text=True, timeout=600, cwd=str(ROOT),
        )
    except (FileNotFoundError, subprocess.TimeoutExpired):
        return None
    if out.returncode != 0:
        return None
    names = {ln.strip() for ln in out.stdout.splitlines() if ln.strip()}
    return names or None


# ── config parsing (indentation-aware mini-parser) ──────────────────────────
def _indent(line: str) -> int:
    return len(line) - len(line.lstrip(" "))


def parse_config(cfg: Path):
    """Return (process, agent_tools, task_texts, agent_count, task_count).

    * agent_tools — list of (agent_name, tool_name, line_no) for every tool item.
    * task_texts  — list of (task_name, text, line_no) joining description/expectedOutput.
    """
    process: str | None = None
    agent_count = 0
    task_count = 0
    agent_tools: list[tuple[str, str, int]] = []
    task_texts: list[tuple[str, str, int]] = []

    lines = cfg.read_text(encoding="utf-8", errors="replace").splitlines()

    section: str | None = None
    section_child_indent: int | None = None
    in_tools = False
    tools_indent = -1
    cur_entry = "?"
    # block-scalar / inline capture for task text
    cur_task = "?"
    cur_task_line = 0

    for lineno, raw in enumerate(lines, start=1):
        stripped = raw.strip()
        if not stripped or stripped.startswith("#"):
            continue
        indent = _indent(raw)

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
            if section_child_indent is None:
                section_child_indent = indent
            if indent == section_child_indent and re.match(r"^[A-Za-z0-9_]+:", stripped):
                in_tools = False
                cur_entry = stripped.split(":", 1)[0]
                if section == "agents":
                    agent_count += 1
                else:
                    task_count += 1
                    cur_task = cur_entry
                    cur_task_line = lineno
                continue

            if section == "agents":
                mt = re.match(r"^tools:\s*(\[.*\])?\s*$", stripped)
                if mt and indent > section_child_indent:
                    if mt.group(1) is not None:
                        # inline list: tools: [] or tools: ["a", "b"]
                        in_tools = False
                        for name in re.findall(r"['\"]?([\w-]+)['\"]?", mt.group(1)):
                            agent_tools.append((cur_entry, name, lineno))
                    else:
                        in_tools = True
                        tools_indent = indent
                    continue
                if in_tools:
                    if indent > tools_indent and stripped.startswith("- "):
                        name = stripped[2:].strip().strip("'\"")
                        if name:
                            agent_tools.append((cur_entry, name, lineno))
                        continue
                    if indent <= tools_indent:
                        in_tools = False

            if section == "tasks":
                # Capture description / expectedOutput text (inline or block scalar).
                m = re.match(r"^(description|expectedOutput|expected_output):\s*(.*)$", stripped)
                if m and indent > section_child_indent:
                    val = m.group(2).strip()
                    if val and val not in ("|", ">", "|-", ">-", "|+", ">+"):
                        task_texts.append((cur_task, val.strip("'\""), lineno))
                    # block scalars: capture following deeper lines
                    continue
                # A deeper free-text line (block-scalar continuation) counts as task text.
                if indent > section_child_indent + 1:
                    task_texts.append((cur_task, stripped, lineno))

    return process, agent_tools, task_texts, agent_count, task_count


# ── linting ─────────────────────────────────────────────────────────────────
class Finding:
    __slots__ = ("level", "line", "msg")

    def __init__(self, level: str, line: int, msg: str):
        self.level = level
        self.line = line
        self.msg = msg


def suggest(name: str, allowed: set[str]) -> str:
    close = difflib.get_close_matches(name, allowed, n=1, cutoff=0.6)
    return f" (did you mean '{close[0]}'?)" if close else ""


def lint_config(cfg: Path, allowed: set[str]) -> list[Finding]:
    findings: list[Finding] = []
    try:
        process, agent_tools, task_texts, n_agents, n_tasks = parse_config(cfg)
    except Exception as exc:  # noqa: BLE001 — surface any parse failure as an error
        return [Finding("error", 0, f"config.yaml failed to parse: {exc}")]

    # (c) structure
    if not process:
        findings.append(Finding("error", 0, "missing top-level 'process:'"))
    if n_agents == 0:
        findings.append(Finding("error", 0, "no agents defined under 'agents:'"))
    if n_tasks == 0:
        findings.append(Finding("error", 0, "no tasks defined under 'tasks:'"))

    # (a) tool names
    for agent, tool, line in agent_tools:
        if tool not in allowed:
            findings.append(Finding(
                "error", line,
                f"agent '{agent}': unknown tool '{tool}'{suggest(tool, allowed)}",
            ))

    # (b) data paths (warnings only)
    example_dir = cfg.parent
    seen_paths: set[str] = set()
    for task, text, line in task_texts:
        for m in DATA_PATH_RE.finditer(text):
            rel = m.group(1).strip()
            # Only flag paths that clearly target the example's own data/ folder.
            if not rel.startswith("data/") and "/data/" not in rel:
                continue
            if rel in seen_paths:
                continue
            seen_paths.add(rel)
            target = (example_dir / rel).resolve()
            if not target.exists():
                findings.append(Finding(
                    "warning", line,
                    f"task '{task}' references data path '{rel}' which does not exist",
                ))
    return findings


TS_HEADER_RE = re.compile(r"^//\s*orkeon-example:\s*(\{.*\})\s*$", re.MULTILINE)
KNOWN_PROCESSES = {"sequential", "hierarchical", "parallel", "consensual", "graph", "autonomous"}
MODULE_TOOLS_DIR_NAME = "_tools"


def module_tool_names(examples_root: Path) -> set[str]:
    """Tool names declared by the shared TypeScript module (EX-01):
    every `.name("x")` in examples/03-finance-trading/_tools/*.ts."""
    names: set[str] = set()
    module = examples_root / "03-finance-trading" / MODULE_TOOLS_DIR_NAME
    if not module.is_dir():
        return names
    for ts in module.glob("*.ts"):
        names.update(re.findall(r"\.name\(\s*\"([a-z0-9_]+)\"\s*\)", ts.read_text(encoding="utf-8", errors="replace")))
    return names


def lint_script(ts: Path, allowed: set[str]) -> list[Finding]:
    """Light lint of a migrated TypeScript crew: the metadata header must be a
    valid JSON one-liner with a known process, and every QUOTED tool name in the
    script (the .tools([...]) YAML-parity surface) must exist — either a built-in
    from the standard manifest or a tool the shared _tools module declares.
    Instance-based wiring (withAutonomousTool) carries its own names and is
    guaranteed by `orkeon run --validate` in CI, not here."""
    findings: list[Finding] = []
    text = ts.read_text(encoding="utf-8", errors="replace")

    m = TS_HEADER_RE.search(text)
    if not m:
        findings.append(Finding("error", 0,
            "missing the `// orkeon-example: {\"process\": ...}` metadata header"))
    else:
        try:
            header = json.loads(m.group(1))
            proc = header.get("process")
            if proc not in KNOWN_PROCESSES:
                findings.append(Finding("error", 0,
                    f"header declares unknown process '{proc}' (expected one of {sorted(KNOWN_PROCESSES)})"))
        except ValueError as exc:
            findings.append(Finding("error", 0, f"metadata header is not valid JSON: {exc}"))

    tool_blobs = " ".join(re.findall(r"\.tools\(\[(.*?)\]\)", text, re.DOTALL))
    for name in sorted(set(re.findall(r"\"([a-z0-9_]+)\"", tool_blobs))):
        if name in allowed:
            continue
        suggestion = difflib.get_close_matches(name, sorted(allowed), n=1)
        hint = f" (did you mean '{suggestion[0]}'?)" if suggestion else ""
        findings.append(Finding("error", 0, f"unknown tool name '{name}'{hint}"))
    return findings


def category_runner(category: str) -> str:
    return "trading" if category == TRADING_CATEGORY else "standard"


def iter_examples():
    for cat in sorted(EXAMPLES.iterdir()):
        if not cat.is_dir() or not CATEGORY_RE.match(cat.name):
            continue
        for sub in sorted(cat.iterdir()):
            if not sub.is_dir() or not EXAMPLE_RE.match(sub.name):
                continue
            cfg = sub / "config.yaml"
            if cfg.is_file():
                yield cat.name, cfg
                continue
            ts = sub / "main.ork.ts"
            if ts.is_file():
                yield cat.name, ts


def main() -> int:
    ap = argparse.ArgumentParser(description="Lint example config.yaml tool names / structure.")
    ap.add_argument("--manifest", type=Path, default=DEFAULT_STANDARD_MANIFEST,
                    help="standard runner tool manifest (one name per line)")
    ap.add_argument("--trading-manifest", type=Path, default=DEFAULT_TRADING_MANIFEST,
                    help="trading runner extra-tools manifest (one name per line)")
    ap.add_argument("--list-tools", action="store_true",
                    help="regenerate manifests by invoking each runner's --list-tools")
    ap.add_argument("--root", type=Path, default=None,
                    help="override the examples scan root (defaults to ./examples)")
    args = ap.parse_args()

    global EXAMPLES
    if args.root:
        EXAMPLES = args.root.resolve()

    if args.list_tools:
        std = manifest_from_list_tools("standard")
        trd = manifest_from_list_tools("trading")
        if std is None:
            print("warning: --list-tools failed for standard runner; falling back to committed manifest",
                  file=sys.stderr)
            std = load_manifest(args.manifest)
        if trd is None:
            print("warning: --list-tools failed for trading runner; falling back to committed manifest",
                  file=sys.stderr)
            trd = load_manifest(args.trading_manifest)
    else:
        std = load_manifest(args.manifest)
        trd = load_manifest(args.trading_manifest)

    standard_allowed = set(std)
    trading_allowed = set(std) | set(trd)

    total = 0
    n_err = 0
    n_warn = 0
    files_with_findings = 0

    script_allowed = standard_allowed | module_tool_names(EXAMPLES)

    for category, cfg in iter_examples():
        total += 1
        if cfg.suffix == ".ts":
            findings = lint_script(cfg, script_allowed)
        else:
            allowed = trading_allowed if category_runner(category) == "trading" else standard_allowed
            findings = lint_config(cfg, allowed)
        if not findings:
            continue
        files_with_findings += 1
        rel = cfg.relative_to(EXAMPLES.parent if not args.root else EXAMPLES)
        print(f"\n{rel}")
        for f in sorted(findings, key=lambda x: (x.level != "error", x.line)):
            loc = f":{f.line}" if f.line else ""
            tag = "ERROR " if f.level == "error" else "warn  "
            print(f"  {tag}{rel}{loc}: {f.msg}")
            if f.level == "error":
                n_err += 1
            else:
                n_warn += 1

    print(f"\n{'─' * 60}")
    print(f"Linted {total} example config(s) — {n_err} error(s), {n_warn} warning(s) "
          f"across {files_with_findings} file(s).")
    return 1 if n_err else 0


if __name__ == "__main__":
    raise SystemExit(main())
