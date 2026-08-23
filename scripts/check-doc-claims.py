#!/usr/bin/env python3
"""Check the numeric claims in the documentation against the code (DOC-02, lot F).

Ground truth is computed from the repository itself; the claims checked are the
ones that have historically rotted (provider/tool/example/project counts and the
version string). A mismatch fails the build with an actionable message.

Run from the repository root: python3 scripts/check-doc-claims.py
"""

from __future__ import annotations

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

    if ERRORS:
        print(f"\ncheck-doc-claims FAILED — {len(ERRORS)} problem(s):")
        for e in ERRORS:
            print(f"  ::error::{e}")
        return 1
    print("check-doc-claims passed: documentation counts match the code.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
