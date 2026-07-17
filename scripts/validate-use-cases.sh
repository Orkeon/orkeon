#!/usr/bin/env bash
# validate-use-cases.sh — Auditeur pérenne du catalogue « 101 Cas d'Utilisation ».
#
# Vérifie que le catalogue marketing reste ancré dans le code source :
#   1. Chaque identifiant back-tické des champs Outils / Process / Mémoire existe
#      dans src/**/*.cs (classe ou Name d'outil), ou est explicitement marqué 🔮.
#   2. Tout `Process` cité ∈ {Sequential, Hierarchical, Consensual, Parallel, Graph,
#      Autonomous} + mécanismes documentés en légende {FlowEngine, A2A}.
#   3. Aucune ligne `**Outils**` ne contient d'interface (`I[A-Z]…`).
#   4. Code retour ≠ 0 si une incohérence subsiste (utilisable en CI).
#
# Usage : bash scripts/validate-use-cases.sh [chemin/vers/101-USE-CASES.md] [chemin/src]
set -uo pipefail

FILE="${1:-project/marketing/content-strategy/101-USE-CASES.md}"
SRC="${2:-src}"

if [[ ! -f "$FILE" ]]; then echo "❌ Catalogue introuvable : $FILE" >&2; exit 2; fi
if [[ ! -d "$SRC"  ]]; then echo "❌ Dossier source introuvable : $SRC" >&2; exit 2; fi

ALLOWED_PROCESS=" Sequential Hierarchical Consensual Parallel Graph Autonomous FlowEngine A2A "
ALLOWED_MEMORY=" InMemory Redis SQLite EncryptedRedis EncryptedSQLite Composite "

errors=0
checked=0

# Pré-indexation de src (une seule passe) : noms de types + Name d'outils back-tickés.
INDEX="$(mktemp)"
trap 'rm -f "$INDEX"' EXIT
{
  # Noms de types déclarés (class/record/interface/enum/struct), génériques tronqués.
  grep -rhoE '(class|record|interface|enum|struct) [A-Za-z_][A-Za-z0-9_]*' \
       --include="*.cs" "$SRC" | awk '{print $2}'
  # Noms d'outils exposés comme chaînes (ex. "index_codebase").
  grep -rhoE '"[a-z_][a-z0-9_]+"' --include="*.cs" "$SRC" | tr -d '"'
} | sort -u > "$INDEX"

# Un identifiant existe s'il est défini comme type OU exposé comme Name d'outil.
exists_in_src() { grep -qxF "$1" "$INDEX"; }

# awk extrait, pour chaque champ pertinent : FIELD <TAB> TOKEN <TAB> FLAG_PLANIFIE(0/1)
extract() {
  awk -F'`' '
    function emit(field,   i, tok, after, flag) {
      for (i = 2; i <= NF; i += 2) {
        tok   = $i
        after = (i + 1 <= NF) ? $(i + 1) : ""
        flag  = (after ~ /🔮/) ? 1 : 0
        if (tok != "") printf "%s\t%s\t%d\n", field, tok, flag
      }
    }
    /^- \*\*Outils\*\*/  { emit("Outils");  next }
    /^- \*\*Process\*\*/ { emit("Process"); next }
    /^- \*\*Mémoire\*\*/ { emit("Memoire"); next }
  ' "$FILE"
}

while IFS=$'\t' read -r field tok flag; do
  checked=$((checked + 1))
  case "$field" in
    Process)
      # Les rôles d'agent (…Agent) sont parfois cités dans la description du Process.
      [[ "$tok" == *Agent ]] && continue
      if [[ "$ALLOWED_PROCESS" != *" $tok "* ]]; then
        echo "❌ Process invalide (hors 6 ProcessType + mécanismes documentés) : \`$tok\`"
        errors=$((errors + 1))
      fi
      ;;
    Memoire)
      [[ "$flag" -eq 1 ]] && continue
      if [[ "$ALLOWED_MEMORY" != *" $tok "* ]]; then
        echo "❌ Mémoire invalide : \`$tok\`"
        errors=$((errors + 1))
      fi
      ;;
    Outils)
      if [[ "$tok" =~ ^I[A-Z] ]]; then
        echo "❌ Interface dans le champ Outils (doit aller en Features) : \`$tok\`"
        errors=$((errors + 1))
        continue
      fi
      [[ "$flag" -eq 1 ]] && continue
      if ! exists_in_src "$tok"; then
        echo "❌ Outil introuvable dans $SRC : \`$tok\`"
        errors=$((errors + 1))
      fi
      ;;
  esac
done < <(extract)

echo "—"
echo "Identifiants contrôlés (Outils/Process/Mémoire) : $checked"
if [[ "$errors" -eq 0 ]]; then
  echo "✅ Catalogue cohérent avec le code source."
  exit 0
else
  echo "❌ $errors incohérence(s) détectée(s)."
  exit 1
fi
