#!/usr/bin/env bash
# Runs lintian on Debian packages and sorts its tags into expected ones (listed,
# with their reason, in scripts/package-deb.lintian-overrides) and unexpected ones.
# Prints a Markdown report (for $GITHUB_STEP_SUMMARY) on stdout.
#
# Usage:
#   scripts/lintian-deb.sh [--overrides FILE] DEB...
#
# Exit status: 0 when no unexpected error (E:) tag remains, 1 when one does,
# 2 when lintian could not run. Unexpected warnings are reported, not failed on.
# The release workflow runs it without blocking: the report is the deliverable.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OVERRIDES="$REPO_ROOT/scripts/package-deb.lintian-overrides"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --overrides) OVERRIDES="$2"; shift 2 ;;
    -h|--help) sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    -*) echo "Unknown option: $1" >&2; exit 2 ;;
    *) break ;;
  esac
done
[[ $# -ge 1 ]] || { echo "usage: lintian-deb.sh [--overrides FILE] DEB..." >&2; exit 2; }
command -v lintian >/dev/null 2>&1 || { echo "lintian not found (install the 'lintian' package)." >&2; exit 2; }
[[ -f "$OVERRIDES" ]] || { echo "No overrides file: $OVERRIDES" >&2; exit 2; }

# The overrides: `<tag> [<context glob>]`, comments and blank lines skipped.
override_tags=(); override_globs=()
while IFS= read -r line || [[ -n "$line" ]]; do
  line="${line%%#*}"
  read -r tag context <<<"$line" || true
  [[ -n "${tag:-}" ]] || continue
  # Only `*` is a wildcard: escape the rest of bash's pattern syntax, the
  # brackets lintian puts around a path first of all.
  glob="${context:-*}"
  glob="${glob//\\/\\\\}"; glob="${glob//\[/\\[}"; glob="${glob//\]/\\]}"; glob="${glob//\?/\\?}"
  override_tags+=("$tag"); override_globs+=("$glob")
done < "$OVERRIDES"

expected() { # $1=tag $2=rest of the line
  local i
  for i in "${!override_tags[@]}"; do
    # The glob is matched unquoted on purpose: `*` in an override is a wildcard.
    # shellcheck disable=SC2053
    [[ "${override_tags[$i]}" == "$1" && "$2" == ${override_globs[$i]} ]] && return 0
  done
  return 1
}

status=0
for deb in "$@"; do
  name="$(basename "$deb")"
  set +e
  out="$(lintian --tag-display-limit 0 "$deb" 2>&1)"
  rc=$?
  set -e
  declare -A kept=()
  unexpected=()
  errors=0
  while IFS= read -r line; do
    # E: orkeon: tag context [path]   (an optional " (binary)" after the package)
    if [[ "$line" =~ ^([EWIPX]):\ [^:]+:\ ([A-Za-z0-9.+-]+)(.*)$ ]]; then
      sev="${BASH_REMATCH[1]}"; tag="${BASH_REMATCH[2]}"; rest="${BASH_REMATCH[3]# }"
      if expected "$tag" "$rest"; then
        kept["$sev $tag"]=$(( ${kept["$sev $tag"]:-0} + 1 ))
      else
        unexpected+=("$line")
        [[ "$sev" == E ]] && errors=$((errors + 1))
      fi
    fi
  done <<<"$out"
  if [[ $rc -ne 0 && ${#unexpected[@]} -eq 0 && ${#kept[@]} -eq 0 ]]; then
    echo "lintian failed on $deb (exit $rc):" >&2
    echo "$out" >&2
    exit 2
  fi

  echo "### lintian: \`$name\`"
  echo
  if [[ ${#unexpected[@]} -eq 0 ]]; then
    echo "No unexpected tag."
  else
    echo "**${#unexpected[@]} unexpected tag(s)**, $errors of them error(s) — fix the package, or add an override with its reason to \`scripts/package-deb.lintian-overrides\`:"
    echo
    echo '```'
    printf '%s\n' "${unexpected[@]}"
    echo '```'
  fi
  if [[ ${#kept[@]} -gt 0 ]]; then
    echo
    echo "Expected (overridden, reasons in \`scripts/package-deb.lintian-overrides\`):"
    echo
    for key in $(printf '%s\n' "${!kept[@]}" | tr ' ' '|' | sort); do
      echo "- \`${key//|/ }\` × ${kept[${key//|/ }]}"
    done
  fi
  echo
  [[ $errors -eq 0 ]] || status=1
  unset kept
done
exit $status
