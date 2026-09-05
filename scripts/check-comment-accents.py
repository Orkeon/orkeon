#!/usr/bin/env python3
"""Fails when a C# comment carries an accented letter, or when a CI/build file
carries French.

Orkeon's invariant: what the maintainers write for other maintainers is English
and never accented. The point is not purity — it is that a comment quoting an
accented UI label ("Modele d'IA") cites something that does not exist, and an
accent-stripped French sentence is still French, only worse. So the check is
deliberately narrow, and narrow in two different ways depending on the file:

  * tracked ``*.cs`` — comment lines only, accented LETTERS only. A user-facing
    string keeps its accents; that is settled, and the whole product would read
    wrong otherwise. This repository uses em dashes, ellipses, arrows and box
    glyphs heavily; none of those are accents and all of them stay. U+00D7 and
    U+00F7 sit inside the Latin-1 letter block but are multiplication and
    division signs, so they are excluded by name.

  * tracked ``.github/workflows/*.yml`` and the repo's ``Directory.Build.props``
    files — EVERY line, accented letters AND a short list of unambiguous French
    words. These files have no user-facing prose: a workflow's ``echo`` lands in
    a PUBLIC CI log and an MSBuild comment is read by contributors, so French
    there is the same defect as a French comment, and the accent test alone
    misses it ("Avant:" / "Apres:" are invisible to it). The word list is
    deliberately tiny and word-bounded so it cannot fire on English or on an
    identifier.

Deliberately NOT covered: docs/fr/**, examples/** and any other place where
French is the content. This check never leaves the two scopes above.

Run: python3 scripts/check-comment-accents.py
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

# Unaccented French that an accent test cannot see. Word-bounded, and kept to
# words that are not English and not plausible identifiers or shell variables.
FRENCH_WORDS = re.compile(
    r"(?i)\b("
    r"ainsi|alors|aucun|aucune|avant|apres|avec|cette|chaque|comme|dans|depuis|"
    r"doit|doivent|donc|entre|etre|fichier|fichiers|importer|jamais|leur|leurs|"
    r"mais|meme|nous|parce|pour|pourquoi|puis|quand|sans|sinon|sont|sous|"
    r"toujours|tous|toute|toutes|tres"
    r")\b"
)


def _tracked(*patterns: str) -> list[Path]:
    out = subprocess.run(
        ["git", "ls-files", *patterns], cwd=ROOT, capture_output=True, text=True, check=True
    ).stdout.split()
    return [ROOT / p for p in out]


def _lines(path: Path) -> list[str]:
    try:
        return path.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeDecodeError):
        return []


def cs_violations(path: Path) -> list[tuple[int, str]]:
    """Accented letter in a C# comment line."""
    found = []
    for number, line in enumerate(_lines(path), 1):
        stripped = line.lstrip()
        if not stripped.startswith(COMMENT_START):
            continue
        if ACCENTED.search(line):
            found.append((number, f"accented letter in a comment: {stripped[:120]}"))
    return found


def ci_violations(path: Path) -> list[tuple[int, str]]:
    """Accented letter or French word anywhere in a workflow / MSBuild props file."""
    found = []
    for number, line in enumerate(_lines(path), 1):
        stripped = line.strip()
        if ACCENTED.search(line):
            found.append((number, f"accented letter in a CI/build file: {stripped[:120]}"))
            continue
        match = FRENCH_WORDS.search(line)
        if match:
            found.append(
                (number, f"French word '{match.group(0)}' in a CI/build file: {stripped[:120]}")
            )
    return found


def main() -> int:
    targets: list[tuple[Path, list[tuple[int, str]]]] = []
    for path in _tracked("*.cs"):
        targets.append((path, cs_violations(path)))
    for path in _tracked(".github/workflows/*.yml", "*Directory.Build.props"):
        targets.append((path, ci_violations(path)))

    total = 0
    files = 0
    for path, found in targets:
        if not found:
            continue
        files += 1
        total += len(found)
        rel = path.relative_to(ROOT)
        for number, text in found:
            print(f"  ::error file={rel},line={number}::{text}")

    if total:
        print(
            f"\ncheck-comment-accents FAILED - {total} line(s) in {files} file(s).\n"
            "Comments are English and unaccented, and workflows / Directory.Build.props are\n"
            "English throughout (their echoes land in public CI logs). Translate the sentence\n"
            "rather than stripping its accents, and translate a quoted UI label rather than\n"
            "citing a spelling the product does not use."
        )
        return 1

    print("check-comment-accents passed: comments are unaccented and CI/build files are English.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
