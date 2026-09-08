#!/usr/bin/env python3
"""Check the numeric claims in the documentation against the code (DOC-02, lot F).

Ground truth is computed from the repository itself; the claims checked are the
ones that have historically rotted (provider/tool/example/project counts and the
version string). A mismatch fails the build with an actionable message.

It also carries two release-safety gates that need no build, so CI can run them on
every pull request through this one step (LOT K): the NuGet lineup must read the same
in its six hand-maintained copies, and scripts/check-package-closure.py's source-mode
checks must pass -- otherwise a closure regression is only discovered by publish.yml,
on a tag that is already cut.

Run from the repository root: python3 scripts/check-doc-claims.py
"""

from __future__ import annotations

import importlib.util
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ERRORS: list[str] = []


def fail(msg: str) -> None:
    ERRORS.append(msg)


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


# --- ground truth -----------------------------------------------------------------------

def gt_version() -> str:
    props = read("src/Directory.Build.props")
    prefix = re.search(r"<VersionPrefix>([^<]+)</VersionPrefix>", props).group(1)
    suffix_m = re.search(r"<VersionSuffix>([^<]+)</VersionSuffix>", props)
    return f"{prefix}-{suffix_m.group(1)}" if suffix_m else prefix


def gt_llm_providers() -> int:
    llms = ROOT / "src/core/Orkeon.Infrastructure/LLMs"
    count = 0
    for f in llms.glob("*.cs"):
        if re.search(r":\s*(OpenAICompatibleProviderBase|HttpLlmProviderBase)\b",
                     f.read_text(encoding="utf-8")):
            count += 1
    return count


def tool_files() -> list[Path]:
    # Skipped: interfaces, doubles, adapters and decorators that match the glob without
    # being built-in tools (ObservedTool is BUS-03's instrumentation decorator).
    skip = {"IBaseTool.cs", "MockTool.cs", "JsTool.cs", "ObservedTool.cs"}
    return [
        f for f in (ROOT / "src").rglob("*Tool.cs")
        if not any(part in ("obj", "bin", "obj-linux") for part in f.parts)
        and f.name not in skip
    ]


def gt_tool_classes() -> int:
    return len(tool_files())


def gt_tool_names() -> set[str]:
    """The agent-visible name of every built-in tool: the positional UniqueName of the
    [ToolContract("…")] attribute when the file carries one, else the literal of a
    `Name => "…"` property (the two mechanisms ToolBase resolves, in that order)."""
    names: set[str] = set()
    for f in tool_files():
        text = f.read_text(encoding="utf-8")
        m = re.search(r'\[ToolContract\(\s*"([^"]+)"', text) or \
            re.search(r'string Name\s*=>\s*"([^"]+)"', text)
        if m:
            names.add(m.group(1))
        else:
            fail(f"{f.relative_to(ROOT)}: cannot extract the tool name "
                 f'(no [ToolContract("…")] and no Name => "…")')
    return names


def gt_examples() -> int:
    index = read("examples/INDEX.md")
    m = re.search(r"\*\*(\d+)\*\* numbered crew examples", index) or \
        re.search(r"the (\d+) numbered crew examples", index)
    if m:
        return int(m.group(1))
    # fallback: count table rows that link into a category directory
    return len(re.findall(r"^\| \[?`?\d+", index, flags=re.M))


def gt_src_projects() -> int:
    return len([f for f in (ROOT / "src").rglob("*.csproj")
                if "obj" not in f.parts and "bin" not in f.parts])


def gt_test_projects() -> int:
    return len([f for f in (ROOT / "tests").rglob("*.csproj")
                if "obj" not in f.parts and "bin" not in f.parts])


# --- claim checks -----------------------------------------------------------------------

def expect_contains(path: str, needle: str, why: str) -> None:
    if needle not in read(path):
        fail(f"{path}: expected to state {needle!r} ({why})")


def expect_absent(path: str, pattern: str, why: str) -> None:
    hits = [
        f"{path}:{i + 1}" for i, line in enumerate(read(path).splitlines())
        if re.search(pattern, line)
    ]
    for hit in hits:
        fail(f"{hit}: stale pattern /{pattern}/ ({why})")


# --- NuGet lineup: six hand-written copies, one truth ------------------------------------

def load_closure_module():
    """scripts/check-package-closure.py is not an importable module name (hyphens), and
    it is the file that owns the lineup constant and the source-mode checks."""
    spec = importlib.util.spec_from_file_location(
        "check_package_closure", ROOT / "scripts/check-package-closure.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def lineup_from_publish_gate() -> list[str]:
    """The --lineup arguments of publish.yml's closure-gate step."""
    m = re.search(r"check-package-closure\.py[^\n]*--lineup ([^\n]+)",
                  read(".github/workflows/publish.yml"))
    if not m:
        fail(".github/workflows/publish.yml: no `--lineup ...` closure-gate arguments found")
        return []
    return m.group(1).split()


def lineup_from_publish_push() -> list[str]:
    """The ids of publish.yml's NuGet.org push loop."""
    m = re.search(r"for id in ([^;]+); do", read(".github/workflows/publish.yml"))
    if not m:
        fail(".github/workflows/publish.yml: no `for id in ...; do` NuGet.org push loop found")
        return []
    return m.group(1).split()


def lineup_from_matrix(path: str) -> list[str]:
    """The PackageIds of the publication matrix' lineup table, in table order. One cell
    may name several ids (the ONNX reranker pair ships as one row)."""
    text = read(path)
    m = re.search(r"^## [^\n]*lineup[^\n]*$", text, flags=re.M | re.I)
    if not m:
        fail(f"{path}: no '## ... lineup ...' section heading found")
        return []
    section_text = re.split(r"^## ", text[m.end():], maxsplit=1, flags=re.M)[0]
    ids: list[str] = []
    for row in re.findall(r"^\|([^|]+)\|", section_text, flags=re.M):
        for pkg_id in re.findall(r"`(Orkeon[A-Za-z0-9.]*)`", row):
            if pkg_id not in ids:
                ids.append(pkg_id)
    return ids


def lineup_from_contributing(path: str) -> list[str]:
    """The PackageIds spelled out in CONTRIBUTING's release process, in push order.
    This copy is prose rather than a table or a shell loop, which is exactly why it was
    the one nobody compared: a maintainer following the release steps reads it, and it
    is the only copy that also states the push order in words."""
    text = read(path)
    heading = r"^## Release Process$" if not path.endswith(".fr.md") else r"^## Processus de release$"
    m = re.search(heading, text, flags=re.M)
    if not m:
        fail(f"{path}: no release-process section heading found")
        return []
    section = re.split(r"^## ", text[m.end():], maxsplit=1, flags=re.M)[0]
    ids: list[str] = []
    for pkg_id in re.findall(r"`(Orkeon[A-Za-z0-9.]*)`", section):
        if pkg_id not in ids:
            ids.append(pkg_id)
    return ids


def check_lineup_copies(canonical: list[str]) -> None:
    """The six lineup ids are typed independently in six places and nothing compared
    them until now: publish.yml's closure-gate arguments, publish.yml's push loop, the
    publication matrix (EN + its FR mirror), CONTRIBUTING's release process (EN + FR),
    and check-package-closure.py's LINEUP.
    Any of them going stale is how a package silently stops being published -- or worse,
    keeps being published after the matrix says it was discontinued."""
    copies = {
        ".github/workflows/publish.yml (closure-gate --lineup)": lineup_from_publish_gate(),
        ".github/workflows/publish.yml (NuGet.org push loop)": lineup_from_publish_push(),
        "docs/reference/publication-matrix.md (lineup table)":
            lineup_from_matrix("docs/reference/publication-matrix.md"),
        "docs/fr/reference/publication-matrix.md (tableau du lineup)":
            lineup_from_matrix("docs/fr/reference/publication-matrix.md"),
        "CONTRIBUTING.md (release process)": lineup_from_contributing("CONTRIBUTING.md"),
        "CONTRIBUTING.fr.md (processus de release)":
            lineup_from_contributing("CONTRIBUTING.fr.md"),
    }
    for where, ids in copies.items():
        if not ids:
            continue  # already reported by the parser
        if set(ids) != set(canonical):
            fail(f"{where}: lineup {sorted(ids)} differs from "
                 f"scripts/check-package-closure.py LINEUP {sorted(canonical)}")

    # The push order is load-bearing, not cosmetic: `Orkeon` must go first because every
    # other lineup package depends on it and NuGet orders nothing (the rc.1/rc.2 shape).
    ordered = [
        ".github/workflows/publish.yml (NuGet.org push loop)",
        "CONTRIBUTING.md (release process)",
        "CONTRIBUTING.fr.md (processus de release)",
    ]
    for where in ordered:
        ids = copies[where]
        if ids and ids[0] != canonical[0]:
            fail(f"{where}: the lineup starts with {ids[0]!r}; {canonical[0]!r} must come "
                 f"first (its dependents would expose an unrestorable package otherwise)")


# --- private submodules must not leak into anything a public reader reads ----------------

# `experiments/` and `backstage/` are private submodules: a public clone gets empty
# directories. A pointer into them is not a broken link -- docfx renders code spans as
# literal text and stays green -- it is a dead end the reader only discovers by trying.
# The 2026-09-07 sweep found 24 such lines across 14 files, plus 46 in `src/**` XML doc
# comments that docfx republishes under `api/**.yml`.
#
# Four patterns, because each catches what the others miss. Pattern 3 matches the
# DISCLAIMER, not the pointer: the state this gate exists to prevent was reached by adding
# a warning instead of removing the reference.
PRIVATE_LEAK_PATTERNS: list[tuple[str, str]] = [
    (r"(?<![\w/.-])(experiments|backstage)/", "path into a private submodule"),
    (r"\bexp ?-?0\d\b", "private experiment codename (exp07, exp 02, ...)"),
    (
        r"private [`*_]*(experiments|backstage)[`*_]* submodule|sous-module priv\u00e9|submodule priv\u00e9",
        "disclaimer about a private submodule -- remove the pointer instead of warning about it",
    ),
    (r"`chapters?/\d|See chapter \d", "private design-archive chapter"),
]

# Legitimately mentions the submodules, and stays out of the scan:
#   .gitmodules, .gitignore      -- the declarations themselves
#   CLAUDE.md                    -- maintainer-facing; docfx.json excludes it from the site
#   scripts/smoke-onboarding/*   -- maintainer tooling that operates on those paths
#   examples/others/README.md    -- its `backstage/` is github.com/backstage/backstage,
#                                   a third-party OSS corpus used as a benchmark
PRIVATE_LEAK_EXCLUDED = {"examples/others/README.md"}


# Directory names never walked: build output, dependency trees, and the vendored
# packages cache. Pruning them at the directory level rather than filtering paths
# afterwards is what keeps this gate under a second -- `rglob` descends into
# `obj-linux/` first and pays 16 s per extension for the privilege.
PRIVATE_LEAK_PRUNED_DIRS = {"bin", "obj", "obj-linux", "node_modules", "packages",
                           ".git", "artifacts", "_site", "TestResults"}

# What each root contributes to the scan, by suffix.
PRIVATE_LEAK_ROOTS: list[tuple[str, tuple[str, ...]]] = [
    ("docs", (".md",)),
    ("examples", (".md",)),          # README.md and the example pages beside them
    ("src", (".cs", ".ts", ".js", ".csproj", ".props", ".targets")),
    ("tests", (".cs",)),
    ("installers", (".wxs",)),
    (".github", (".yml",)),
]


def private_leak_files() -> list[Path]:
    """Everything a public reader reads.

    Four groups, and the last three were added after a review found the first one alone
    left more uncovered than it covered:

      the site           -- docs/ in both languages, the root pages, the example READMEs;
      what docfx ships   -- `src/**/*.cs` XML comments, republished under `api/**.yml`;
      what NuGet ships   -- the typings concatenated into `dist/orkeon.d.ts`, and
                            `src/**/*.js|*.ts`: `forge-assistant.ork.js` is an
                            EmbeddedResource of the `orkeon` tool, so its header reaches
                            every consumer who never clones the repo;
      what a clone reads -- `tests/**/*.cs` (22 live pointers when this was widened),
                            the build files, the installer and the Dockerfiles. A test
                            comment is not published, but it is the first thing a
                            contributor opens, and it must not cite what they cannot see.
    """
    out: list[Path] = []
    seen: set[Path] = set()

    def add(path: Path) -> None:
        if not path.is_file() or path in seen:
            return
        seen.add(path)
        if path.relative_to(ROOT).as_posix() in PRIVATE_LEAK_EXCLUDED:
            return
        out.append(path)

    # CHANGELOG.md IS scanned: docfx publishes it as CHANGELOG.html and indexes it for
    # search, so it is documentation by any definition a reader would use. It carried seven
    # private pointers when this was widened. Rewording one to drop an unfollowable path is
    # not rewriting history -- every measurement, date and gap id was kept.
    for name in ROOT.glob("*.md"):
        if name.name != "CLAUDE.md":
            add(name)
    for name in ROOT.glob("Dockerfile*"):
        add(name)
    add(ROOT / "Directory.Build.props")

    for root_name, suffixes in PRIVATE_LEAK_ROOTS:
        root = ROOT / root_name
        if not root.is_dir():
            continue
        for dirpath, dirnames, filenames in os.walk(root):
            dirnames[:] = [d for d in dirnames if d not in PRIVATE_LEAK_PRUNED_DIRS]
            for filename in filenames:
                if filename.endswith(suffixes):
                    add(Path(dirpath) / filename)

    return sorted(out)


def check_private_submodule_leaks() -> None:
    """Fail on any pointer to a private submodule in public-facing material.

    Deliberately NOT covered, so this gate is not itself a false claim: git history and
    commit messages; the private submodules' own contents; `scripts/` tooling, which has
    to spell the names to match them; the files listed in PRIVATE_LEAK_EXCLUDED; and
    non-text assets. Walks with rglob rather than a git pathspec because
    `git grep -- 'docs/**/*.md'` silently skips `docs/INDEX.md` -- the globstar needs a
    directory level, and a gate must not have that failure mode.
    """
    compiled = [(re.compile(pat), why) for pat, why in PRIVATE_LEAK_PATTERNS]
    for path in private_leak_files():
        rel = path.relative_to(ROOT).as_posix()
        try:
            lines = path.read_text(encoding="utf-8").splitlines()
        except UnicodeDecodeError:
            continue
        for i, line in enumerate(lines):
            for rx, why in compiled:
                if rx.search(line):
                    fail(f"{rel}:{i + 1}: points at private material ({why}): {line.strip()[:100]}")


def main() -> int:
    version = gt_version()
    providers = gt_llm_providers()
    tools = gt_tool_classes()
    examples = gt_examples()
    src_projects = gt_src_projects()
    test_projects = gt_test_projects()

    print(f"ground truth: version={version} providers={providers} tool-classes={tools} "
          f"examples={examples} src-projects={src_projects} test-projects={test_projects}")

    # Version — the single source of truth must be echoed correctly.
    expect_contains("CLAUDE.md", f"`{version}`", "version from src/Directory.Build.props")
    expect_contains("README.md", f"**{version}**", "version from src/Directory.Build.props")
    expect_contains("README.fr.md", f"**{version}**", "version from src/Directory.Build.props")

    # LLM provider count.
    expect_contains("README.md", f"{providers} LLM provider",
                    f"{providers} concrete providers in src/core/Orkeon.Infrastructure/LLMs")
    expect_contains("CLAUDE.md", f"{providers} LLM provider", "provider count")
    expect_contains("docs/INDEX.md", f"{providers} providers", "provider count")
    expect_contains("docs/fr/INDEX.md", f"{providers} providers", "provider count")
    expect_contains("README.fr.md", f"{providers} fournisseurs LLM", "provider count")

    # Tool-name parity: docs/tools/inventory.md is the reference catalogue — every
    # built-in tool must be named there (both languages), every name its tables list
    # must exist in the code, and its summary total must match. This is the gate that
    # keeps "shipped but undocumented" (xlsx_reader) and "documented but nonexistent"
    # (bing_search) from ever coming back.
    tool_names = gt_tool_names()
    for path in ("docs/tools/inventory.md", "docs/fr/tools/inventory.md"):
        text = read(path)
        for name in sorted(tool_names):
            if f"`{name}`" not in text:
                fail(f"{path}: built-in tool `{name}` is not documented")
        documented = set(re.findall(r"^\| `([a-z][a-z0-9_]*)` \|", text, flags=re.M))
        for name in sorted(documented - tool_names):
            fail(f"{path}: documents `{name}`, which matches no built-in tool class")
        if f"**{tools}**" not in text:
            fail(f"{path}: the per-package summary total must state {tools}")

    # Tool-class count: CLAUDE.md states it exactly; READMEs use a "N+" floor.
    expect_contains("CLAUDE.md", f"{tools} built-in tool classes", "count of *Tool.cs under src/")
    for path in ("README.md", "README.fr.md", "docs/INDEX.md", "docs/fr/INDEX.md"):
        m = re.search(r"\b(\d+)\+ (?:built-in tools|tools by category|outils)", read(path))
        if not m:
            fail(f"{path}: no 'N+' tool-count claim found")
        elif not (0 < tools - int(m.group(1)) <= 15):
            fail(f"{path}: tool floor {m.group(1)}+ is out of range for the real count {tools} "
                 f"(keep the floor within 15 of reality)")

    # Examples count.
    for path in ("README.md", "README.fr.md"):
        expect_contains(path, f"{examples} ", f"example count from examples/INDEX.md")

    # Project counts.
    expect_contains("CLAUDE.md", f"**{src_projects} src projects**", "csproj count under src/")
    expect_contains("CLAUDE.md", f"**{test_projects} test projects**", "csproj count under tests/")

    # Known-stale patterns that must never come back (outside legitimate history).
    for path in ("README.md", "README.fr.md", "docs/INDEX.md", "docs/fr/INDEX.md"):
        expect_absent(path, r"\b12(th|e|ᵉ)? (LLM )?(provider|fournisseur)", "Gemini is the 13th provider")

    # Release-safety gates that need no build, so they run on every PR here rather than
    # only on the tag (LOT K): the six copies of the NuGet lineup, and the csproj-only
    # half of the package-closure gate that publish.yml otherwise runs after `dotnet pack`.
    check_private_submodule_leaks()

    closure = load_closure_module()
    check_lineup_copies(closure.LINEUP)
    for e in closure.source_errors(list(closure.LINEUP)):
        fail(f"check-package-closure (source mode): {e}")

    if ERRORS:
        print(f"\ncheck-doc-claims FAILED — {len(ERRORS)} problem(s):")
        for e in ERRORS:
            print(f"  ::error::{e}")
        return 1
    print("check-doc-claims passed: documentation counts match the code.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
