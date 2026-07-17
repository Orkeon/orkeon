#!/usr/bin/env python3
"""validate_use_cases.py — Auditeur pérenne du catalogue « 101 Cas d'Utilisation ».

Référence canonique multiplateforme (équivalent de validate-use-cases.sh / .ps1).

Vérifie que le catalogue marketing reste ancré dans le code source :
  1. Chaque identifiant back-tické des champs Outils / Process / Mémoire existe dans
     src/**/*.cs (type déclaré ou Name d'outil), ou est explicitement marqué 🔮.
  2. Tout `Process` cité ∈ {Sequential, Hierarchical, Consensual, Parallel, Graph,
     Autonomous} + mécanismes documentés en légende {FlowEngine, A2A}
     (+ rôles d'agent `…Agent` tolérés dans la description).
  3. Aucune ligne `**Outils**` ne contient d'interface (`I[A-Z]…`).

Code retour ≠ 0 si une incohérence subsiste (utilisable en CI).
Usage : python3 scripts/validate_use_cases.py [catalogue.md] [dossier_src]
"""
from __future__ import annotations
import re
import sys
from pathlib import Path

ALLOWED_PROCESS = {"Sequential", "Hierarchical", "Consensual", "Parallel",
                   "Graph", "Autonomous", "FlowEngine", "A2A"}
ALLOWED_MEMORY = {"InMemory", "Redis", "SQLite", "EncryptedRedis",
                  "EncryptedSQLite", "Composite"}

FIELD_RE = re.compile(r"^- \*\*(Outils|Process|Mémoire)\*\*\s*:\s*(.*)$")
TOKEN_RE = re.compile(r"`([^`]+)`(\s*🔮)?")
TYPE_RE = re.compile(r"\b(?:class|record|interface|enum|struct)\s+([A-Za-z_][A-Za-z0-9_]*)")
NAME_RE = re.compile(r'"([a-z_][a-z0-9_]+)"')


def build_index(src: Path) -> set[str]:
    idx: set[str] = set()
    for cs in src.rglob("*.cs"):
        try:
            text = cs.read_text(encoding="utf-8", errors="ignore")
        except OSError:
            continue
        idx.update(TYPE_RE.findall(text))
        idx.update(NAME_RE.findall(text))
    return idx


def main() -> int:
    catalog = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(
        "project/marketing/content-strategy/101-USE-CASES.md")
    src = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("src")
    if not catalog.is_file():
        print(f"❌ Catalogue introuvable : {catalog}", file=sys.stderr)
        return 2
    if not src.is_dir():
        print(f"❌ Dossier source introuvable : {src}", file=sys.stderr)
        return 2

    index = build_index(src)
    errors = 0
    checked = 0

    for line in catalog.read_text(encoding="utf-8").splitlines():
        m = FIELD_RE.match(line)
        if not m:
            continue
        field, rest = m.group(1), m.group(2)
        for tok_m in TOKEN_RE.finditer(rest):
            tok = tok_m.group(1)
            planned = tok_m.group(2) is not None
            checked += 1
            if field == "Process":
                if tok.endswith("Agent"):
                    continue
                if tok not in ALLOWED_PROCESS:
                    print(f"❌ Process invalide (hors 6 ProcessType + mécanismes) : `{tok}`")
                    errors += 1
            elif field == "Mémoire":
                if planned:
                    continue
                if tok not in ALLOWED_MEMORY:
                    print(f"❌ Mémoire invalide : `{tok}`")
                    errors += 1
            elif field == "Outils":
                if re.match(r"^I[A-Z]", tok):
                    print(f"❌ Interface dans le champ Outils (doit aller en Features) : `{tok}`")
                    errors += 1
                    continue
                if planned:
                    continue
                # tolère les génériques éventuels (Foo<Bar>) en ne gardant que le nom
                base = tok.split("<", 1)[0]
                if base not in index:
                    print(f"❌ Outil introuvable dans {src} : `{tok}`")
                    errors += 1

    print("—")
    print(f"Identifiants contrôlés (Outils/Process/Mémoire) : {checked}")
    if errors == 0:
        print("✅ Catalogue cohérent avec le code source.")
        return 0
    print(f"❌ {errors} incohérence(s) détectée(s).")
    return 1


if __name__ == "__main__":
    sys.exit(main())
