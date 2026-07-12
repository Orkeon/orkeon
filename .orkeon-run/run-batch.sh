#!/usr/bin/env bash
# Lance une liste d'exemples Orkeon (manifest TSV: cfgpath<TAB>hasData<TAB>trading)
# via l'image orkeon-runners:test, modèle ai/gemma4 (DMR host), verbose 1.
# Usage: run-batch.sh <manifest.tsv> <label>
set -u

MAN="$1"
LABEL="${2:-batch}"
MODEL="ai/gemma4"
IMG="orkeon-runners:test"
HOST_BASE='C:\Users\cyril\Claude\orkeon\.orkeon-run'
LIN_BASE=/workspace/.orkeon-run
TIMEOUT=900
SUMMARY="$LIN_BASE/SUMMARY.md"

mkdir -p "$LIN_BASE/logs"
if [ ! -f "$SUMMARY" ]; then
  printf '# Runs des exemples Orkeon — modele %s (verbose 1)\n\n' "$MODEL" > "$SUMMARY"
  printf '| # | Exemple | Runner | Statut | Exit | Duree | Out | LLMx | 1re erreur |\n' >> "$SUMMARY"
  printf '|---|---------|--------|--------|------|-------|-----|------|------------|\n' >> "$SUMMARY"
fi

i=0
while IFS=$'\t' read -r cfg hasData trading; do
  [ -z "$cfg" ] && continue
  i=$((i+1))
  rel=${cfg#examples/}
  cat=$(printf '%s' "$rel" | cut -d/ -f1)
  ex=$(printf '%s' "$rel" | cut -d/ -f2)
  name="${cat%%-*}-${ex}"
  outdir_lin="$LIN_BASE/${name}-output"
  outdir_host="${HOST_BASE}\\${name}-output"
  log="$LIN_BASE/logs/${name}.log"
  cname="orkeon_${LABEL}_${i}"
  mkdir -p "$outdir_lin"

  if [ "$hasData" = "1" ]; then
    mounts="/app/${cfg%/config.yaml}/data:/data:ro /output:/output:rw"
  else
    mounts="/output:/output:rw"
  fi

  printf '[%s] (%d) RUN %-40s trading=%s data=%s\n' "$(date +%H:%M:%S)" "$i" "$name" "$trading" "$hasData"

  start=$SECONDS
  if [ "$trading" = "1" ]; then
    timeout "$TIMEOUT" docker run --rm --name "$cname" \
      --add-host=host.docker.internal:host-gateway \
      -v "${outdir_host}:/output" \
      -e ORKEON_RUNNER=trading -e ORKEON_Llm__Model="$MODEL" -e ORKEON_NO_BANNER=1 \
      -e ORKEON_RateLimiting__MaxConcurrentRequests=1 \
      "$IMG" --config "$cfg" --mount $mounts --verbose 2 --llm-log-path /output/llm-logs > "$log" 2>&1
    rc=$?
  else
    timeout "$TIMEOUT" docker run --rm --name "$cname" \
      --add-host=host.docker.internal:host-gateway \
      -v "${outdir_host}:/output" \
      -e ORKEON_Llm__Model="$MODEL" -e ORKEON_NO_BANNER=1 \
      -e ORKEON_RateLimiting__MaxConcurrentRequests=1 \
      "$IMG" run "$cfg" --mount $mounts --verbose 2 --llm-log-path /output/llm-logs > "$log" 2>&1
    rc=$?
  fi
  dur=$((SECONDS-start))
  [ $rc -eq 124 ] && docker rm -f "$cname" >/dev/null 2>&1

  if [ $rc -eq 0 ]; then status=OK; elif [ $rc -eq 124 ]; then status=TIMEOUT; else status=FAIL; fi
  nout=$(find "$outdir_lin" -type f -not -path '*/llm-logs/*' 2>/dev/null | wc -l | tr -d ' ')
  nllm=$(find "$outdir_lin/llm-logs" -type f 2>/dev/null | wc -l | tr -d ' ')
  err=$(grep -m1 -iE 'error|exception|unhandled|not found|refused|fail' "$log" 2>/dev/null | head -c 110 | tr '|\n\t' '   ')
  runner=$([ "$trading" = "1" ] && echo trading || echo orkeon)

  printf '| %d | %s | %s | %s | %d | %ss | %s | %s | %s |\n' \
    "$i" "$name" "$runner" "$status" "$rc" "$dur" "$nout" "$nllm" "$err" >> "$SUMMARY"
  printf '   -> %s exit=%d %ss out=%s\n' "$status" "$rc" "$dur" "$nout"
done < "$MAN"
printf '=== BATCH %s TERMINE (%d exemples) ===\n' "$LABEL" "$i"
