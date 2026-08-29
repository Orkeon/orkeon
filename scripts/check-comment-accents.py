#!/usr/bin/env python3
"""Fails when a C# comment carries an accented letter.

Orkeon's invariant: comments are English and never accented. The point is not
purity — it is that a comment quoting an accented UI label ("Modele d'IA") cites
something that does not exist, and an accent-stripped French sentence is still
French, only worse. So the check is deliberately narrow:

  * comment lines only. A user-facing string keeps its accents; that is settled,
    and the whole product would read wrong otherwise.
  * accented LETTERS only. This repository uses em dashes, ellipses, arrows and
    box glyphs heavily; none of those are accents and all of them stay. U+00D7
    and U+00F7 sit inside the Latin-1 letter block but are multiplication and
    division signs, so they are excluded by name.

Run: python3 scripts/check-comment-accents.py [--fix-brand]
"""
from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Latin-1 Supplement letters + Latin Extended-A, minus the two maths signs.
ACCENTED = re.compile(r"[À-ÖØ-öø-ÿĀ-ſ]")
COMMENT_START = ("//", "*", "/*")


def tracked_cs_files() -> list[Path]:
    out = subprocess.run(
        ["git", "ls-files", "*.cs"], cwd=ROOT, capture_output=True, text=True, check=True
    ).stdout.split()
    return [ROOT / p for p in out]


def violations(path: Path) -> list[tuple[int, str]]:
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeDecodeError):
        return []
    found = []
    for number, line in enumerate(lines, 1):
        stripped = line.lstrip()
        if not stripped.startswith(COMMENT_START):
            continue
        if ACCENTED.search(line):
            found.append((number, stripped[:120]))
    return found


def main() -> int:
    total = 0
    files = 0
    for path in tracked_cs_files():
        found = violations(path)
        if not found:
            continue
        files += 1
        total += len(found)
        rel = path.relative_to(ROOT)
        for number, text in found:
            print(f"  ::error file={rel},line={number}::accented letter in a comment: {text}")

    if total:
        print(
            f"\ncheck-comment-accents FAILED - {total} comment line(s) in {files} file(s).\n"
            "Comments are English and unaccented. Translate the sentence rather than stripping\n"
            "its accents, and translate a quoted UI label rather than citing a spelling the\n"
            "product does not use."
        )
        return 1

    print("check-comment-accents passed: every comment is unaccented.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
