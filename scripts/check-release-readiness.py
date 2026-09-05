#!/usr/bin/env python3
"""Release-readiness gate (LOT K) — the manual release steps, enforced.

CONTRIBUTING's "Release Process" asks for three things before a `v*` tag is
pushed. Step 1 (the tag matching `src/Directory.Build.props`) has been mechanical
since the 0.9.1-beta incident; steps 2 and 3 were pure discipline, and discipline
is what produced that incident. This script makes them mechanical too:

1. CHANGELOG.md's `[Unreleased]` section must be EMPTY. At release time its
   content is cut into a dated version section — a tag whose changes are still
   filed under "Unreleased" ships a changelog that describes no released version,
   and the next release silently inherits the entries.

2. Every `src/**/PublicAPI.Unshipped.txt` must be EMPTY. Unshipped entries move to
   `PublicAPI.Shipped.txt` at release: shipping a public API that is still declared
   "unshipped" leaves the frozen surface lying about what consumers can now call,
   and the next real addition looks like it was already released.
   A file holding only the `#nullable enable` header and blank lines counts as empty.

Run it locally before tagging, exactly as publish.yml runs it on the tag:

    python3 scripts/check-release-readiness.py
"""

from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
# The PublicApiAnalyzers header every API file carries; not content.
API_HEADERS = {"#nullable enable"}


def unreleased_entries() -> list[str]:
    """The non-blank lines of the `## [Unreleased]` section, up to the next `## `
    heading (the link-reference definitions at the bottom of the file live outside
    every section and are never part of it)."""
    lines = (ROOT / "CHANGELOG.md").read_text(encoding="utf-8").splitlines()
    try:
        start = next(i for i, line in enumerate(lines)
                     if line.strip().lower().startswith("## [unreleased]"))
    except StopIteration:
        return ["CHANGELOG.md has no '## [Unreleased]' section at all"]
    body: list[str] = []
    for line in lines[start + 1:]:
        if line.startswith("## "):
            break
        if line.strip():
            body.append(line.rstrip())
    return body


def unshipped_api_files() -> list[tuple[Path, list[str]]]:
    """Every src PublicAPI.Unshipped.txt that still declares an API, with its entries."""
    out: list[tuple[Path, list[str]]] = []
    for f in sorted((ROOT / "src").rglob("PublicAPI.Unshipped.txt")):
        if any(part in ("obj", "bin", "obj-linux") for part in f.parts):
            continue
        entries = [line.rstrip() for line in f.read_text(encoding="utf-8").splitlines()
                   if line.strip() and line.strip() not in API_HEADERS]
        if entries:
            out.append((f, entries))
    return out


def main() -> int:
    errors: list[str] = []

    entries = unreleased_entries()
    if entries:
        preview = "; ".join(e.strip() for e in entries[:3])
        errors.append(
            f"CHANGELOG.md: the [Unreleased] section still holds {len(entries)} line(s) "
            f"({preview}{' ...' if len(entries) > 3 else ''}). "
            f"Cut it into a dated version section before tagging "
            f"(CONTRIBUTING 'Release Process', step 2)."
        )

    for path, api_entries in unshipped_api_files():
        errors.append(
            f"{path.relative_to(ROOT)}: {len(api_entries)} unshipped API entry(ies), "
            f"first is '{api_entries[0].strip()}'. Move them to PublicAPI.Shipped.txt "
            f"before tagging (CONTRIBUTING 'Release Process', step 3)."
        )

    if errors:
        print(f"check-release-readiness FAILED — {len(errors)} problem(s):")
        for e in errors:
            print(f"::error::check-release-readiness: {e}")
        return 1
    print("check-release-readiness passed: [Unreleased] is cut and no unshipped public API remains.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
