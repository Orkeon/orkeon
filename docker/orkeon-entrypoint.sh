#!/bin/sh
# orkeon-entrypoint — dispatcher for the orkeon-runners image (see
# Dockerfile.runners for the full docs). Selects the executable from
# ORKEON_RUNNER, optionally starts the embedded local LLM server (local-llm
# image variant only), then drops root privileges to the bind-mount owner.
set -e

# --- Embedded local LLM (local-llm variant only) -------------------------------
# When the image was built with `--target local-llm`, /app/local-llm holds a
# llama-server binary plus one GGUF model. Serve it on 127.0.0.1:12434 with the
# /engines/llama.cpp prefix so the URL shape matches the Docker Model Runner
# profile every example already uses — the baked default settings then work
# with zero configuration. Runs as the built-in `app` user (never root; it only
# reads /app/local-llm, which is a+rX). The background server survives the
# final `exec gosu ...` below (children persist across exec) and dies with the
# container. Disable with -e ORKEON_LOCAL_LLM=0.
if [ -f /app/local-llm/model.gguf ] && [ "${ORKEON_LOCAL_LLM:-1}" != "0" ]; then
  LD_LIBRARY_PATH=/app/local-llm gosu app:app /app/local-llm/llama-server \
    -m /app/local-llm/model.gguf --host 127.0.0.1 --port 12434 \
    --api-prefix /engines/llama.cpp --alias "${ORKEON_LOCAL_LLM_ALIAS:-local}" \
    -c "${ORKEON_LOCAL_LLM_CTX:-8192}" >/tmp/llama-server.log 2>&1 &
  echo "[orkeon] loading local model ${ORKEON_LOCAL_LLM_ALIAS:-local} (CPU — first load can take 30-90s)..." >&2
  i=0
  until curl -fsS http://127.0.0.1:12434/engines/llama.cpp/health >/dev/null 2>&1; do
    i=$((i+1))
    if [ "$i" -ge 120 ]; then
      echo "[orkeon] local model failed to load after 120s — last log lines:" >&2
      tail -5 /tmp/llama-server.log >&2 || true
      break
    fi
    sleep 1
  done
  [ "$i" -lt 120 ] && echo "[orkeon] local model ready on http://127.0.0.1:12434/engines/llama.cpp/v1" >&2
fi

# --- Runner dispatch ------------------------------------------------------------
case "${ORKEON_RUNNER:-orkeon}" in
  orkeon)       set -- dotnet /app/runners/orkeon/orkeon.dll "$@" ;;
  trading)      set -- dotnet /app/runners/trading/Orkeon.Examples.Trading.Runner.dll "$@" ;;
  repl)         set -- dotnet /app/runners/repl/Orkeon.ConsoleApp.dll "$@" ;;
  shell)        cd /workspace && set -- zsh "$@" ;;
  *) echo "Unknown ORKEON_RUNNER=$ORKEON_RUNNER (expected: orkeon|trading|repl|shell)" >&2; exit 2 ;;
esac

# --- Privilege drop --------------------------------------------------------------
# Adopt the uid:gid owning /workspace (then /output) so files written on bind
# mounts belong to the host user; fall back to the built-in `app` user (1654).
# `docker run --user` bypasses this (id -u != 0); ORKEON_STAY_ROOT=1 keeps root.
if [ "$(id -u)" = "0" ] && [ "${ORKEON_STAY_ROOT:-0}" != "1" ]; then
  runas="app:app"
  for d in /workspace /output; do
    uid="$(stat -c %u "$d" 2>/dev/null || echo 0)"
    if [ "$uid" != "0" ] && [ "$uid" != "1654" ]; then
      runas="$uid:$(stat -c %g "$d")"
      break
    fi
  done
  export HOME=/tmp
  exec gosu "$runas" "$@"
fi
exec "$@"
