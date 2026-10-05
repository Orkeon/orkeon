#!/bin/sh
# /workspace/scripts/prune-worktrees.sh
# Supprime les worktrees dont la branche est mergée dans origin/main,
# ne garde que les sources (pas de bin/obj) dans les autres.
set -u
cd /workspace || exit 1
git fetch --prune

git worktree list --porcelain \
| awk '/^worktree /{w=$2} /^branch refs\/heads\//{sub("refs/heads/","",$2); print w, $2}' \
| while read wt br; do
  [ "$wt" = /workspace ] && continue                              # le dépôt principal
  [ "$br" = main ] && continue
  [ -n "$(git -C "$wt" status --porcelain)" ] && continue         # travail non committé : on ne touche pas

  if git merge-base --is-ancestor "$br" origin/main; then
    git worktree remove --force "$wt" && git branch -D "$br"      # mergé : on supprime tout
  else
    find "$wt" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +   # en cours : sources seules
  fi
done

git worktree prune