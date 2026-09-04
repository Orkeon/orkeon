#!/usr/bin/env python3
"""lint-example-readmes.py — Static lint for the numbered example ``README.md`` files.

Scans every ``examples/NN-category/NN-slug/README.md`` and checks:

  (a) PRESENCE    — the example ships a README.md.

  (b) LAUNCH      — the README contains a launch section (a heading such as
      ``## Run`` / ``## Run it`` / ``## Lancer`` / ``## Running`` …) that carries a
      recognized launch command — ``orkeon run <config>`` (or the from-source
      ``dotnet run --project …Orkeon.Scripting.Cli -- run <config>``) — and that config path
      resolves to a file that exists on disk. A missing or broken launch command is an
      ERROR; a launch command that exists but sits outside a recognized heading is a
      WARNING.

  (c) LINKS       — every relative Markdown link ``[text](path)`` resolves to a file or
      directory (external ``http(s)://`` / ``mailto:`` and pure ``#anchor`` links are
      skipped). Broken relative links are ERRORS.

  (d) INDEX       — ``examples/INDEX.md`` is up to date, verified by delegating to
      ``bash scripts/generate-examples-index.sh --check``. A stale index is an ERROR.

Exit status: non-zero if any ERROR is found; warnings never fail the build.

Usage:
  python3 scripts/lint-example-readmes.py
  python3 scripts/lint-example-readmes.py --no-index-check   # skip the INDEX freshness check
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EXAMPLES = ROOT / "examples"
SCRIPTS = ROOT / "scripts"

CATEGORY_RE = re.compile(r"^\d{2}-")
EXAMPLE_RE = re.compile(r"^(\d+)-(.+)$")

# Headings that introduce a "how to launch this example" section.
LAUNCH_HEADING_RE = re.compile(
    r"^#{2,6}\s+(run(?:\s+it)?|running|lancer|lancement|ex[eé]cuter|usage)\b",
    re.IGNORECASE,
)
HEADING_RE = re.compile(r"^#{1,6}\s+")
# Launch commands, each capturing the crew config path (group 1). A config path is a
# ``.yaml`` / ``.yml`` / ``.ork.ts`` token, so prose ellipses (``… -- run …``) are ignored.
# Commands may span backslash-continued lines, hence DOTALL between tokens.
_CONFIG = r"(\S+\.(?:ya?ml|ork\.ts))"
LAUNCH_CMD_RES = [
    # orkeon run <config>                                      (installed CLI, positional)
    re.compile(r"\borkeon\s+run\s+" + _CONFIG, re.IGNORECASE),
    # dotnet run --project …Orkeon.Scripting.Cli… -- run <config>   (from a source checkout)
    re.compile(
        r"dotnet\s+run\s+--project\s+\S*Scripting\.Cli\S*\s+--\s+run\s+" + _CONFIG,
        re.IGNORECASE | re.DOTALL,
    ),
    # legacy fallback: dotnet run --project <x> … --config|-c <config>
    re.compile(
        r"dotnet\s+run\s+--project\s+\S+.*?(?:--config|(?<!\w)-c)\s+" + _CONFIG,
        re.IGNORECASE | re.DOTALL,
    ),
]
LINK_RE = re.compile(r"(?<!\!)\[[^\]]*\]\(([^)]+)\)")


class Finding:
    __slots__ = ("level", "line", "msg")

    def __init__(self, level: str, line: int, msg: str):
        self.level = level
        self.line = line
        self.msg = msg


def find_launch(text: str) -> tuple[list[str], bool]:
    """Return (config_paths, under_recognized_heading).

    config_paths is every crew config path referenced by a recognized launch command
    (deduplicated). under_recognized_heading is True if such a command appears after a
    launch heading.
    """
    lines = text.splitlines()
    heading_lines: list[int] = [
        i for i, ln in enumerate(lines) if LAUNCH_HEADING_RE.match(ln)
    ]

    def heading_level(ln: str) -> int:
        return len(ln) - len(ln.lstrip("#"))

    # A launch section runs until the next heading of the same or higher level;
    # deeper sub-headings (e.g. "### Via the interactive runner") stay inside it.
    section_ranges = []
    for h in heading_lines:
        level = heading_level(lines[h])
        end = len(lines)
        for j in range(h + 1, len(lines)):
            if HEADING_RE.match(lines[j]) and heading_level(lines[j]) <= level:
                end = j
                break
        section_ranges.append((h, end))

    configs: dict[str, int] = {}  # config path -> first line seen
    for rx in LAUNCH_CMD_RES:
        for m in rx.finditer(text):
            cfg = m.group(1).strip().strip("`\"'")
            line_no = text.count("\n", 0, m.start())
            configs.setdefault(cfg, line_no)

    under_heading = any(
        any(h < ln < end for h, end in section_ranges) for ln in configs.values()
    )
    return list(configs), under_heading


def lint_readme(readme: Path) -> list[Finding]:
    findings: list[Finding] = []
    if not readme.is_file():
        return [Finding("error", 0, "README.md is missing")]

    text = readme.read_text(encoding="utf-8", errors="replace")
    lines = text.splitlines()

    # (b) launch command
    configs, under_heading = find_launch(text)
    if not configs:
        findings.append(Finding(
            "error", 0,
            "no launch command found "
            "('orkeon run <config>' / "
            "'dotnet run --project …Scripting.Cli -- run <config>')",
        ))
    else:
        for cfg in configs:
            target = (ROOT / cfg).resolve()
            if not target.is_file():
                findings.append(Finding(
                    "error", 0, f"launch config path does not exist: {cfg}",
                ))
        if not under_heading:
            findings.append(Finding(
                "warning", 0,
                "launch command is not under a recognized run/launch heading "
                "(## Run / ## Run it / ## Lancer …)",
            ))

    # (c) relative markdown links
    base = readme.parent
    for i, ln in enumerate(lines, start=1):
        for m in LINK_RE.finditer(ln):
            href = m.group(1).split()[0].strip()  # drop optional "title"
            if not href or href.startswith(("http://", "https://", "mailto:", "#")):
                continue
            path_part = href.split("#", 1)[0]
            if not path_part:
                continue
            target = (base / path_part).resolve()
            if not target.exists():
                findings.append(Finding(
                    "error", i, f"broken relative link '{href}' → {path_part}",
                ))
    return findings


def iter_examples():
    for cat in sorted(EXAMPLES.iterdir()):
        if not cat.is_dir() or not CATEGORY_RE.match(cat.name):
            continue
        for sub in sorted(cat.iterdir()):
            if not sub.is_dir() or not EXAMPLE_RE.match(sub.name):
                continue
            if (sub / "config.yaml").is_file() or (sub / "main.ork.ts").is_file():
                yield sub / "README.md"


def check_category_readmes() -> list[tuple[Path, Finding]]:
    """Every example directory of a category must be mentioned in the category README.

    Keeps the per-category tables from silently drifting when an example is added
    (DOC-02/F2 — three category READMEs had missing rows).
    """
    findings: list[tuple[Path, Finding]] = []
    for cat in sorted(EXAMPLES.iterdir()):
        if not cat.is_dir() or not CATEGORY_RE.match(cat.name):
            continue
        readme = cat / "README.md"
        if not readme.is_file():
            findings.append((cat, Finding("error", 0, "category README.md missing")))
            continue
        text = readme.read_text(encoding="utf-8", errors="replace")
        for sub in sorted(cat.iterdir()):
            if not sub.is_dir() or not EXAMPLE_RE.match(sub.name):
                continue
            num = sub.name.split("-", 1)[0]
            # A row mention counts as either the bare number in a table row or the
            # directory name anywhere in the README.
            if sub.name in text or re.search(rf"^\|\s*{num}\s*\|", text, flags=re.M):
                continue
            findings.append((readme, Finding(
                "error", 0,
                f"example '{sub.name}' has no row/mention in the category README",
            )))
    return findings


def check_index() -> Finding | None:
    script = SCRIPTS / "generate-examples-index.sh"
    if not script.is_file():
        return Finding("warning", 0, "generate-examples-index.sh not found; INDEX check skipped")
    try:
        res = subprocess.run(
            ["bash", str(script), "--check"],
            capture_output=True, text=True, timeout=120, cwd=str(ROOT),
        )
    except (FileNotFoundError, subprocess.TimeoutExpired) as exc:
        return Finding("warning", 0, f"could not run INDEX --check: {exc}")
    if res.returncode != 0:
        detail = (res.stdout + res.stderr).strip().splitlines()
        tail = detail[-1] if detail else "examples/INDEX.md is stale"
        return Finding("error", 0, f"examples/INDEX.md is out of date — run "
                                   f"'bash scripts/generate-examples-index.sh' ({tail})")
    return None


def main() -> int:
    ap = argparse.ArgumentParser(description="Lint example README.md launch sections and links.")
    ap.add_argument("--no-index-check", action="store_true",
                    help="skip the examples/INDEX.md freshness check")
    args = ap.parse_args()

    total = 0
    n_err = 0
    n_warn = 0
    files_with_findings = 0

    for readme in iter_examples():
        total += 1
        findings = lint_readme(readme)
        if not findings:
            continue
        files_with_findings += 1
        rel = readme.relative_to(ROOT)
        print(f"\n{rel}")
        for f in sorted(findings, key=lambda x: (x.level != "error", x.line)):
            loc = f":{f.line}" if f.line else ""
            tag = "ERROR " if f.level == "error" else "warn  "
            print(f"  {tag}{rel}{loc}: {f.msg}")
            if f.level == "error":
                n_err += 1
            else:
                n_warn += 1

    for path, f in check_category_readmes():
        rel = path.relative_to(ROOT)
        print(f"\n{rel}")
        tag = "ERROR " if f.level == "error" else "warn  "
        print(f"  {tag}{rel}: {f.msg}")
        if f.level == "error":
            n_err += 1
        else:
            n_warn += 1

    if not args.no_index_check:
        idx = check_index()
        if idx:
            print("\nexamples/INDEX.md")
            tag = "ERROR " if idx.level == "error" else "warn  "
            print(f"  {tag}{idx.msg}")
            if idx.level == "error":
                n_err += 1
            else:
                n_warn += 1

    print(f"\n{'─' * 60}")
    print(f"Linted {total} README.md — {n_err} error(s), {n_warn} warning(s) "
          f"across {files_with_findings} file(s).")
    return 1 if n_err else 0


if __name__ == "__main__":
    raise SystemExit(main())
