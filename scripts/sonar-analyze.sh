#!/usr/bin/env bash
set -euo pipefail

# ── Locale ────────────────────────────────────────────────────────────────────
# Force a UTF-8 locale so the SonarScanner JVM (sun.jnu.encoding) can encode
# non-ASCII source paths (e.g. accented French filenames under project/). This
# image ships only C.utf8 (no en_US.UTF-8 is generated), so without this the
# scanner's project-reactor build crashes with java.nio.file.InvalidPathException
# on the first accented path. C.utf8 keeps C semantics for everything but charset.
export LC_ALL=C.utf8
export LANG=C.utf8

# ══════════════════════════════════════════════════════════════════════════════
# SonarQube Analysis Script — Orkeon
# ══════════════════════════════════════════════════════════════════════════════
# Performs a full SonarQube analysis with code coverage, enforces the
# (blocking) Quality Gate and generates a Markdown report of the results.
#
# Usage:
#   export SONAR_TOKEN="<your-token>"
#   bash scripts/sonar-analyze.sh
#
# Environment variables:
#   SONAR_TOKEN        (required) Authentication token for SonarQube
#   SONAR_HOST_URL     (optional) SonarQube server URL (default: http://localhost:9000)
#   SONAR_PROJECT_KEY  (optional) Project key (default: CrewAI.NET — kept by
#                      maintainer decision, QCM 2026-06-11: renaming the key on
#                      the server would reset the analysis history)
#   SONAR_NO_DOCKER    (optional) Set to 1 to forbid the Docker Compose
#                      fallback when the server is unreachable (implied when
#                      CI=true, e.g. on GitHub Actions)
#
# Quality Gate (chantier R5.4 — BLOCKING):
#   The script provisions an idempotent "Orkeon Transitional" Quality Gate
#   with transitional thresholds (see QUALITY_GATE_CONDITIONS below and
#   docs/guides/quality-gate.md for the hardening trajectory), binds it to the
#   project, runs the analysis with sonar.qualitygate.wait=true, and EXITS
#   WITH A NON-ZERO CODE when the gate is FAILED.
# ══════════════════════════════════════════════════════════════════════════════

# ── Configuration ─────────────────────────────────────────────────────────────
SONAR_HOST="${SONAR_HOST_URL:-http://localhost:9000}"
# Project key kept as "CrewAI.NET" by maintainer decision (QCM 2026-06-11) to
# preserve the analysis history on the SonarQube server.
SONAR_PROJECT_KEY="${SONAR_PROJECT_KEY:-CrewAI.NET}"
SONAR_TOKEN="${SONAR_TOKEN:?Variable SONAR_TOKEN requise. Export it before running this script.}"
SOLUTION_PATH="Orkeon.sln"
COVERAGE_DIR="./coverage"
REPORT_DIR="./sonarqube"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SONAR_PROPS_FILE="$PROJECT_ROOT/sonar-project.properties"
SONAR_PROPS_BACKUP=""
SONARQUBE_WAIT_TIMEOUT=300  # 5 minutes
CE_TASK_WAIT_TIMEOUT=300    # 5 minutes
QUALITY_GATE_NAME="Orkeon Transitional"
QUALITY_GATE_TIMEOUT=600    # seconds the scanner 'end' step waits for the gate verdict
QUALITY_GATE_STATUS="UNKNOWN"  # filled by check_quality_gate (for the summary)

# ── Logging utilities ─────────────────────────────────────────────────────────
log_info()    { printf "\033[1;34m[INFO]\033[0m  %s\n" "$*"; }
log_success() { printf "\033[1;32m[OK]\033[0m    %s\n" "$*"; }
log_warn()    { printf "\033[1;33m[WARN]\033[0m  %s\n" "$*"; }
log_error()   { printf "\033[1;31m[ERROR]\033[0m %s\n" "$*" >&2; }

# ── Cleanup trap ──────────────────────────────────────────────────────────────
cleanup() {
    local exit_code=$?
    if [[ -n "$SONAR_PROPS_BACKUP" && -f "$SONAR_PROPS_BACKUP" ]]; then
        mv "$SONAR_PROPS_BACKUP" "$SONAR_PROPS_FILE"
        log_info "Restored sonar-project.properties"
    fi
    if [[ $exit_code -ne 0 ]]; then
        log_error "Script failed with exit code $exit_code"
    fi
}
trap cleanup EXIT

# ── Prerequisites check ──────────────────────────────────────────────────────
check_prerequisites() {
    log_info "Checking prerequisites..."

    # Ensure ~/.dotnet/tools is in PATH (global tools location)
    if [[ -d "$HOME/.dotnet/tools" ]] && [[ ":$PATH:" != *":$HOME/.dotnet/tools:"* ]]; then
        export PATH="$PATH:$HOME/.dotnet/tools"
    fi

    if ! command -v dotnet &>/dev/null; then
        log_error "'dotnet' CLI not found. Install .NET SDK first."
        exit 1
    fi

    if ! command -v dotnet-sonarscanner &>/dev/null; then
        if dotnet tool list -g 2>/dev/null | grep -qi "dotnet-sonarscanner"; then
            log_warn "'dotnet-sonarscanner' installed but not in PATH. Check ~/.dotnet/tools/"
        else
            log_warn "'dotnet-sonarscanner' not found globally. Installing..."
            dotnet tool install --global dotnet-sonarscanner
        fi
    fi

    # ReportGenerator merges Cobertura files into the SonarQube generic format.
    # Without it no coverage is imported and the new_coverage gate condition is
    # silently NOT evaluated — install it like the scanner above.
    if ! command -v reportgenerator &>/dev/null; then
        if dotnet tool list -g 2>/dev/null | grep -qi "dotnet-reportgenerator-globaltool"; then
            log_warn "'reportgenerator' installed but not in PATH. Check ~/.dotnet/tools/"
        else
            log_warn "'reportgenerator' not found globally. Installing (required for coverage import)..."
            dotnet tool install --global dotnet-reportgenerator-globaltool
        fi
    fi

    if ! command -v curl &>/dev/null; then
        log_error "'curl' not found. It is required for SonarQube health checks."
        exit 1
    fi

    if ! command -v jq &>/dev/null; then
        log_warn "'jq' not found. Markdown report generation will be skipped."
        log_warn "Install jq for full report support: sudo apt-get install jq"
    fi

    log_success "Prerequisites OK"
}

# ── Handle sonar-project.properties (Bug #2) ─────────────────────────────────
handle_sonar_properties() {
    if [[ -f "$SONAR_PROPS_FILE" ]]; then
        SONAR_PROPS_BACKUP="${SONAR_PROPS_FILE}.bak"
        log_warn "Found sonar-project.properties — renaming to .bak to avoid conflict with CLI parameters"
        mv "$SONAR_PROPS_FILE" "$SONAR_PROPS_BACKUP"
    fi
}

# ── Wait for SonarQube to be ready ───────────────────────────────────────────
wait_for_sonarqube() {
    log_info "Waiting for SonarQube to be ready at $SONAR_HOST ..."
    local elapsed=0
    local interval=5

    while [[ $elapsed -lt $SONARQUBE_WAIT_TIMEOUT ]]; do
        local status
        status=$(curl -sf "$SONAR_HOST/api/system/status" 2>/dev/null | grep -o '"status":"[^"]*"' | head -1 | cut -d'"' -f4 || true)

        if [[ "$status" == "UP" ]]; then
            log_success "SonarQube is ready"
            return 0
        fi

        printf "."
        sleep "$interval"
        elapsed=$((elapsed + interval))
    done

    printf "\n"
    log_error "SonarQube did not become ready within ${SONARQUBE_WAIT_TIMEOUT}s"
    exit 1
}

# ── Start SonarQube if not running ───────────────────────────────────────────
ensure_sonarqube_running() {
    local status
    status=$(curl -sf "$SONAR_HOST/api/system/status" 2>/dev/null | grep -o '"status":"[^"]*"' | head -1 | cut -d'"' -f4 || true)

    if [[ "$status" == "UP" ]]; then
        log_success "SonarQube is already running"
        return 0
    fi

    # In CI (or when explicitly disabled) never try to boot a local SonarQube:
    # an ephemeral server would have an empty history and a meaningless gate.
    if [[ "${CI:-}" == "true" || "${SONAR_NO_DOCKER:-}" == "1" ]]; then
        log_error "SonarQube is not reachable at $SONAR_HOST and the Docker fallback is disabled (CI/SONAR_NO_DOCKER)."
        log_error "Point SONAR_HOST_URL to a reachable SonarQube instance."
        exit 1
    fi

    log_info "SonarQube not reachable. Attempting to start via Docker Compose..."

    local compose_file="$PROJECT_ROOT/docker-compose.sonarqube.yml"
    if [[ ! -f "$compose_file" ]]; then
        log_error "docker-compose.sonarqube.yml not found at $compose_file"
        log_error "Start SonarQube manually or set SONAR_HOST_URL to an existing instance."
        exit 1
    fi

    local docker_output
    if ! docker_output=$(docker compose -f "$compose_file" up -d 2>&1); then
        if echo "$docker_output" | grep -qi "whiteout"; then
            log_error "Docker pull failed due to overlayfs whiteout file issue (common in WSL2/DinD)."
            log_error ""
            log_error "Workaround: Switch Docker storage driver to vfs:"
            log_error "  1. Edit /etc/docker/daemon.json and add: {\"storage-driver\": \"vfs\"}"
            log_error "  2. Restart Docker: sudo systemctl restart docker"
            log_error "  3. Re-run this script"
            exit 1
        fi
        log_error "Failed to start SonarQube via Docker Compose:"
        echo "$docker_output" >&2
        exit 1
    fi

    wait_for_sonarqube
}

# ── Wait for Compute Engine task to finish ───────────────────────────────────
wait_for_ce_task() {
    if ! command -v jq &>/dev/null; then
        log_warn "jq not available — skipping CE task wait (analysis may still be processing)"
        sleep 10
        return 0
    fi

    log_info "Waiting for SonarQube background task to complete..."
    local elapsed=0
    local interval=5

    while [[ $elapsed -lt $CE_TASK_WAIT_TIMEOUT ]]; do
        local response
        response=$(curl -sf -u "$SONAR_TOKEN:" "$SONAR_HOST/api/ce/component?component=$SONAR_PROJECT_KEY" 2>/dev/null || true)

        if [[ -z "$response" ]]; then
            sleep "$interval"
            elapsed=$((elapsed + interval))
            continue
        fi

        local task_status
        task_status=$(echo "$response" | jq -r '.current.status // .queue[0].status // "NONE"' 2>/dev/null || echo "UNKNOWN")

        case "$task_status" in
            SUCCESS)
                log_success "Background analysis completed successfully"
                return 0
                ;;
            FAILED|CANCELED)
                log_error "Background analysis task $task_status"
                return 1
                ;;
            NONE)
                # No task found yet, may still be queuing
                ;;
            *)
                # IN_PROGRESS, PENDING, etc.
                ;;
        esac

        printf "."
        sleep "$interval"
        elapsed=$((elapsed + interval))
    done

    printf "\n"
    log_warn "CE task did not complete within ${CE_TASK_WAIT_TIMEOUT}s — report may be incomplete"
}

# ── SonarQube API helper ──────────────────────────────────────────────────────
sonar_api() {
    curl -sf -u "$SONAR_TOKEN:" "$SONAR_HOST$1" 2>/dev/null || echo '{}'
}

# POST helper for Quality Gate provisioning: prints the HTTP status code on
# stdout; the response body is written to /tmp/sonar_gate_post_body.json.
sonar_gate_api_post() {
    local endpoint="$1"; shift
    local curl_args=()
    local param
    for param in "$@"; do
        curl_args+=(--data-urlencode "$param")
    done
    local code
    code=$(curl -s -o /tmp/sonar_gate_post_body.json -w "%{http_code}" \
        -u "$SONAR_TOKEN:" -X POST "${curl_args[@]}" "$SONAR_HOST$endpoint" 2>/dev/null) || true
    echo "${code:-000}"
}

# ── Quality Gate provisioning (R5.4 — « assouplir puis durcir ») ──────────────
# Provisions the transitional Quality Gate decided by the maintainer (QCM
# 2026-06-11): the gate is BLOCKING immediately, with realistic *transitional*
# thresholds, then hardened along the documented trajectory.
#
# Single source of truth for the thresholds: the table below, mirrored in
# scripts/sonar-analyze.ps1 ($QualityGateConditions) — keep both in sync.
# Hardening trajectory and exit criteria: docs/guides/quality-gate.md.
#
# Conditions (all on NEW code; op = comparator that triggers the ERROR):
#   metric                          op  transitional  final  rationale (fiche R5.4)
#   new_coverage                    LT  70            80     new code at 71.3% on 2026-05-31; aligned
#                                                            with the 70% CI coverage gate (R5.2);
#                                                            raised to 75 then 80 after R5.5/R5.6
#   new_reliability_rating          GT  2 (≤ B)       1 (A)  B tolerates MINOR bugs while the 3 known
#                                                            MAJOR bugs are fixed; the condition stays
#                                                            red until then — intended pressure
#   new_security_rating             GT  1 (A)         1 (A)  already met — kept strict
#   new_maintainability_rating      GT  1 (A)         1 (A)  already met — kept strict
#   new_duplicated_lines_density    GT  3             3      already met (0.56%) — kept
#   new_security_hotspots_reviewed  LT  0             100    neutralised (always OK, stays visible)
#                                                            until the 9 inherited hotspots are
#                                                            reviewed (security remediation, fiche 07)
QUALITY_GATE_CONDITIONS=(
    "new_coverage|LT|70"
    "new_reliability_rating|GT|2"
    "new_security_rating|GT|1"
    "new_maintainability_rating|GT|1"
    "new_duplicated_lines_density|GT|3"
    "new_security_hotspots_reviewed|LT|0"
)

provision_quality_gate() {
    log_info "Provisioning Quality Gate \"$QUALITY_GATE_NAME\" (transitional thresholds — R5.4)..."

    # 1. Create the gate if it does not exist yet (idempotent).
    local show_json
    show_json=$(curl -s -u "$SONAR_TOKEN:" --get \
        --data-urlencode "name=$QUALITY_GATE_NAME" \
        "$SONAR_HOST/api/qualitygates/show" 2>/dev/null) || show_json=""
    if [[ -z "$show_json" || "$show_json" == *'"errors"'* ]]; then
        local create_code
        create_code=$(sonar_gate_api_post "/api/qualitygates/create" "name=$QUALITY_GATE_NAME")
        if [[ "$create_code" != 2* ]]; then
            log_warn "Could not create Quality Gate \"$QUALITY_GATE_NAME\" (HTTP $create_code)."
            log_warn "The token needs the 'Administer Quality Gates' permission."
            log_warn "Continuing — the gate currently assigned to the project will be enforced instead."
            return 0
        fi
        log_success "Quality Gate \"$QUALITY_GATE_NAME\" created"
        show_json=$(curl -s -u "$SONAR_TOKEN:" --get \
            --data-urlencode "name=$QUALITY_GATE_NAME" \
            "$SONAR_HOST/api/qualitygates/show" 2>/dev/null) || show_json=""
    fi

    # 2. Create or update each condition. Idempotent updates need jq (to read
    #    existing condition ids); without jq, conditions are only created and
    #    pre-existing thresholds are left untouched.
    local entry metric op error existing_id existing_error cond_code
    for entry in "${QUALITY_GATE_CONDITIONS[@]}"; do
        IFS='|' read -r metric op error <<< "$entry"

        existing_id=""
        existing_error=""
        if command -v jq &>/dev/null; then
            existing_id=$(echo "$show_json" | jq -r --arg m "$metric" \
                '[.conditions[]? | select(.metric == $m)][0].id // empty' 2>/dev/null) || existing_id=""
            existing_error=$(echo "$show_json" | jq -r --arg m "$metric" \
                '[.conditions[]? | select(.metric == $m)][0].error // empty' 2>/dev/null) || existing_error=""
        fi

        if [[ -n "$existing_id" ]]; then
            if [[ "$existing_error" == "$error" ]]; then
                continue  # already provisioned with the expected threshold
            fi
            cond_code=$(sonar_gate_api_post "/api/qualitygates/update_condition" \
                "id=$existing_id" "metric=$metric" "op=$op" "error=$error")
            if [[ "$cond_code" == 2* ]]; then
                log_info "  Condition updated: $metric $op $error"
            else
                log_warn "  Could not update condition '$metric' (HTTP $cond_code)"
            fi
        else
            cond_code=$(sonar_gate_api_post "/api/qualitygates/create_condition" \
                "gateName=$QUALITY_GATE_NAME" "metric=$metric" "op=$op" "error=$error")
            if [[ "$cond_code" == 2* ]]; then
                log_info "  Condition created: $metric $op $error"
            elif [[ "$cond_code" == "400" ]] && ! command -v jq &>/dev/null; then
                :  # condition already exists — cannot diff thresholds without jq, leave as-is
            else
                log_warn "  Could not create condition '$metric' (HTTP $cond_code)"
            fi
        fi
    done

    # 3. Bind the gate to the project. On a brand-new server the project does
    #    not exist before its first analysis — provision it, then retry.
    local select_code
    select_code=$(sonar_gate_api_post "/api/qualitygates/select" \
        "gateName=$QUALITY_GATE_NAME" "projectKey=$SONAR_PROJECT_KEY")
    if [[ "$select_code" == "404" ]]; then
        log_info "Project '$SONAR_PROJECT_KEY' not found on the server — provisioning it..."
        sonar_gate_api_post "/api/projects/create" \
            "project=$SONAR_PROJECT_KEY" "name=$SONAR_PROJECT_KEY" >/dev/null
        select_code=$(sonar_gate_api_post "/api/qualitygates/select" \
            "gateName=$QUALITY_GATE_NAME" "projectKey=$SONAR_PROJECT_KEY")
    fi
    if [[ "$select_code" == 2* ]]; then
        log_success "Quality Gate \"$QUALITY_GATE_NAME\" assigned to project '$SONAR_PROJECT_KEY'"
    else
        log_warn "Could not assign the gate to project '$SONAR_PROJECT_KEY' (HTTP $select_code) — the currently assigned gate will be enforced."
    fi
}

# ── Enforce the Quality Gate verdict (R5.4 — blocking gate) ──────────────────
# Reads the gate status from the API (robust complement to
# sonar.qualitygate.wait, cf. fiche R5.4 alternative) and prints every failed
# condition so the verdict is visible in the logs. Returns 1 when the gate is
# in ERROR (or any non-OK computed status).
check_quality_gate() {
    log_info "Fetching Quality Gate verdict for '$SONAR_PROJECT_KEY'..."
    local response
    response=$(sonar_api "/api/qualitygates/project_status?projectKey=$SONAR_PROJECT_KEY")

    if command -v jq &>/dev/null; then
        QUALITY_GATE_STATUS=$(echo "$response" | jq -r '.projectStatus.status // "UNKNOWN"' 2>/dev/null) || QUALITY_GATE_STATUS="UNKNOWN"
        echo "$response" | jq -r '
            .projectStatus.conditions[]? | select(.status != "OK") |
            "        ✗ \(.metricKey): actual \(.actualValue // "-") vs threshold \(.errorThreshold // "-") [\(.status)]"
        ' >&2 || true
    else
        QUALITY_GATE_STATUS=$(echo "$response" | grep -o '"status":"[^"]*"' | head -1 | cut -d'"' -f4 || true)
    fi
    QUALITY_GATE_STATUS="${QUALITY_GATE_STATUS:-UNKNOWN}"

    case "$QUALITY_GATE_STATUS" in
        OK)
            log_success "Quality Gate: OK"
            return 0
            ;;
        NONE|UNKNOWN)
            log_warn "Quality Gate status is '$QUALITY_GATE_STATUS' (no gate computed or API unreachable) — not failing on an unknown verdict"
            return 0
            ;;
        *)
            log_error "Quality Gate: $QUALITY_GATE_STATUS — gate \"$QUALITY_GATE_NAME\" is blocking (see failed conditions above)"
            return 1
            ;;
    esac
}

# ── Fetch all issues with pagination ─────────────────────────────────────────
fetch_all_issues() {
    local query="$1"
    local page=1
    local page_size=500
    local all_issues="/tmp/sonar_all_issues.json"
    local page_file="/tmp/sonar_page_issues.json"

    echo '[]' > "$all_issues"

    while true; do
        local response
        response=$(curl -s -u "$SONAR_TOKEN:" "$SONAR_HOST/api/issues/search?${query}&ps=${page_size}&p=${page}" 2>/dev/null || echo '{}')
        local count
        count=$(echo "$response" | jq '.issues | length' 2>/dev/null || echo "0")

        if [[ "$count" -eq 0 ]]; then
            break
        fi

        # Extract issues to temp file, then merge
        echo "$response" | jq '[.issues[]]' > "$page_file"
        jq -s '.[0] + .[1]' "$all_issues" "$page_file" > "${all_issues}.tmp"
        mv "${all_issues}.tmp" "$all_issues"

        local total
        total=$(echo "$response" | jq '.paging.total // 0')
        local fetched=$((page * page_size))
        if [[ $fetched -ge $total ]]; then
            break
        fi
        page=$((page + 1))
    done

    cat "$all_issues"
    rm -f "$page_file"
}

# ── Generate Markdown report ─────────────────────────────────────────────────
generate_markdown_report() {
    if ! command -v jq &>/dev/null; then
        log_warn "jq not installed — skipping Markdown report generation"
        return 0
    fi

    log_info "Generating Markdown report..."

    mkdir -p "$REPORT_DIR"
    local report_date
    report_date=$(date +%Y-%m-%d)
    local report_file="$REPORT_DIR/sonarqube-report-${report_date}.md"
    local pk="$SONAR_PROJECT_KEY"

    # ── Source & test project modules (derived from the solution) ──
    # Extract every .csproj path from Orkeon.sln, normalise it to a directory
    # path, and split into source (src/) and test (tests/) modules. Deriving the
    # list from the solution keeps the report in sync with reality instead of a
    # hand-maintained array that silently drops newly added projects (CLI,
    # Tools.*, Analysis, Scripting, analyzers, …). Example projects live under
    # examples/ and are intentionally excluded from the scan, so they are not
    # listed here either.
    local _sln_dirs
    _sln_dirs=$(grep -oE '"[^"]+\.csproj"' "$SOLUTION_PATH" \
        | tr -d '"' | tr '\\' '/' \
        | sed -E 's#/[^/]+\.csproj$##' \
        | sort -u)

    local PROJECTS=()
    local TEST_PROJECTS=()
    local _dir
    while IFS= read -r _dir; do
        [[ -z "$_dir" ]] && continue
        case "$_dir" in
            src/*)   PROJECTS+=("$_dir") ;;
            tests/*) TEST_PROJECTS+=("$_dir") ;;
        esac
    done <<< "$_sln_dirs"

    log_info "  Modules détectés : ${#PROJECTS[@]} source, ${#TEST_PROJECTS[@]} test"

    # ══════════════════════════════════════════════════════════════════════════
    # 1. Fetch all data
    # ══════════════════════════════════════════════════════════════════════════
    log_info "  Fetching Quality Gate..."
    local qg_response
    qg_response=$(sonar_api "/api/qualitygates/project_status?projectKey=$pk")

    log_info "  Fetching global metrics..."
    local measures_response
    measures_response=$(sonar_api "/api/measures/component?component=$pk&metricKeys=bugs,vulnerabilities,code_smells,coverage,duplicated_lines_density,ncloc,sqale_index,sqale_debt_ratio,reliability_rating,security_rating,sqale_rating,security_hotspots,cognitive_complexity")

    log_info "  Fetching issues facets..."
    local issues_facets_response
    issues_facets_response=$(sonar_api "/api/issues/search?componentKeys=$pk&ps=1&facets=severities,types&resolved=false")

    log_info "  Fetching all issues (paginated)..."
    local all_issues
    all_issues=$(fetch_all_issues "componentKeys=$pk&resolved=false")
    local total_issues
    total_issues=$(echo "$all_issues" | jq 'length')
    log_info "  Fetched $total_issues issues"

    log_info "  Fetching security hotspots..."
    local hotspots_response
    hotspots_response=$(sonar_api "/api/hotspots/search?projectKey=$pk&ps=500")

    log_info "  Fetching per-project metrics..."
    declare -A project_metrics
    for proj in "${PROJECTS[@]}"; do
        project_metrics["$proj"]=$(sonar_api "/api/measures/component?component=$pk:$proj&metricKeys=coverage,ncloc,bugs,vulnerabilities,code_smells,duplicated_lines_density,cognitive_complexity")
    done

    # Per-directory coverage is fetched per project inside the loop below
    # (component_tree caps at ps=500; a single solution-wide query truncates the
    # larger modules — Domain/Infrastructure were silently dropped).

    # ══════════════════════════════════════════════════════════════════════════
    # 2. Build report
    # ══════════════════════════════════════════════════════════════════════════

    # Helper functions for jq
    local get_metric='def get(k): .component.measures[]? | select(.metric == k) | .value // "-"'
    local rating_map='def rating(v): if v == "1.0" or v == "1" then "A" elif v == "2.0" or v == "2" then "B" elif v == "3.0" or v == "3" then "C" elif v == "4.0" or v == "4" then "D" elif v == "5.0" or v == "5" then "E" else v end'

    # ── Quality Gate ──
    local qg_status
    qg_status=$(echo "$qg_response" | jq -r '.projectStatus.status // "UNKNOWN"')
    local qg_icon="❓"
    [[ "$qg_status" == "OK" ]] && qg_icon="✅"
    [[ "$qg_status" == "ERROR" ]] && qg_icon="❌"

    cat > "$report_file" <<HEADER
# Rapport d'Analyse SonarQube — Orkeon

**Date** : ${report_date}  |  **Dashboard** : [Ouvrir SonarQube](${SONAR_HOST}/dashboard?id=${pk})

---

## Quality Gate : ${qg_icon} ${qg_status}

| Condition | Statut | Valeur | Seuil |
|-----------|--------|--------|-------|
HEADER

    echo "$qg_response" | jq -r '
        .projectStatus.conditions[]? |
        "| \(.metricKey) | \(if .status == "OK" then "✅" elif .status == "ERROR" then "❌" else "⚠️" end) \(.status) | \(.actualValue) | \(.errorThreshold // "-") |"
    ' >> "$report_file" 2>/dev/null || echo "| _Aucune condition disponible_ | - | - | - |" >> "$report_file"

    # ── Global Metrics ──
    cat >> "$report_file" <<'SECTION'

---

## Métriques globales

| Métrique | Valeur |
|----------|--------|
SECTION

    echo "$measures_response" | jq -r "
        $get_metric;
        \"| Lignes de code | \(get(\"ncloc\")) |\",
        \"| Bugs | \(get(\"bugs\")) |\",
        \"| Vulnérabilités | \(get(\"vulnerabilities\")) |\",
        \"| Code Smells | \(get(\"code_smells\")) |\",
        \"| Couverture | \(get(\"coverage\"))% |\",
        \"| Duplication | \(get(\"duplicated_lines_density\"))% |\",
        \"| Dette technique (min) | \(get(\"sqale_index\")) |\",
        \"| Ratio dette | \(get(\"sqale_debt_ratio\"))% |\",
        \"| Hotspots sécurité | \(get(\"security_hotspots\")) |\",
        \"| Complexité cognitive | \(get(\"cognitive_complexity\")) |\"
    " >> "$report_file" 2>/dev/null || echo "| _Métriques non disponibles_ | - |" >> "$report_file"

    cat >> "$report_file" <<'SECTION'

### Ratings

| Catégorie | Rating |
|-----------|--------|
SECTION

    echo "$measures_response" | jq -r "
        $get_metric; $rating_map;
        \"| Fiabilité | \(get(\"reliability_rating\") | rating) |\",
        \"| Sécurité | \(get(\"security_rating\") | rating) |\",
        \"| Maintenabilité | \(get(\"sqale_rating\") | rating) |\"
    " >> "$report_file" 2>/dev/null || echo "| _Ratings non disponibles_ | - |" >> "$report_file"

    # ══════════════════════════════════════════════════════════════════════════
    # 3. Coverage par projet
    # ══════════════════════════════════════════════════════════════════════════
    cat >> "$report_file" <<'SECTION'

---

## Couverture par projet

| Projet | Lignes | Couverture | Bugs | Code Smells | Duplication | Complexité |
|--------|--------|------------|------|-------------|-------------|------------|
SECTION

    for proj in "${PROJECTS[@]}"; do
        local proj_name
        proj_name=$(basename "$proj")
        echo "${project_metrics[$proj]}" | jq -r "
            $get_metric;
            \"| **$proj_name** | \(get(\"ncloc\")) | \(get(\"coverage\"))% | \(get(\"bugs\")) | \(get(\"code_smells\")) | \(get(\"duplicated_lines_density\") // \"-\")% | \(get(\"cognitive_complexity\") // \"-\") |\"
        " >> "$report_file" 2>/dev/null
    done

    # ══════════════════════════════════════════════════════════════════════════
    # 4. Coverage par dossier (source uniquement)
    # ══════════════════════════════════════════════════════════════════════════
    for proj in "${PROJECTS[@]}"; do
        local proj_name
        proj_name=$(basename "$proj")

        cat >> "$report_file" <<SECTION

### ${proj_name} — Couverture par dossier

| Dossier | Lignes | Couverture | Bugs | Code Smells |
|---------|--------|------------|------|-------------|
SECTION

        # Fetch the directory tree scoped to this project (stays under the ps=500 cap).
        local proj_dirs
        proj_dirs=$(sonar_api "/api/measures/component_tree?component=$pk:$proj&metricKeys=coverage,ncloc,bugs,code_smells&qualifier=DIR&ps=500&s=path")

        echo "$proj_dirs" | jq -r --arg prefix "$proj/" '
            [.components[]? | select(.measures | map(select(.metric == "ncloc")) | length > 0)] |
            sort_by(.path) | .[]? |
            {
                path: .path,
                ncloc:      ([.measures[]? | select(.metric == "ncloc")      | .value] | first // "-"),
                coverage:   ([.measures[]? | select(.metric == "coverage")   | .value] | first // "-"),
                bugs:       ([.measures[]? | select(.metric == "bugs")       | .value] | first // "0"),
                code_smells:([.measures[]? | select(.metric == "code_smells")| .value] | first // "0")
            } |
            "| `\(.path | ltrimstr($prefix))` | \(.ncloc) | \(.coverage)% | \(.bugs) | \(.code_smells) |"
        ' >> "$report_file" 2>/dev/null

    done

    # ══════════════════════════════════════════════════════════════════════════
    # 5. Issues par sévérité et type (résumé)
    # ══════════════════════════════════════════════════════════════════════════
    cat >> "$report_file" <<'SECTION'

---

## Résumé des issues

### Par sévérité

| Sévérité | Nombre |
|----------|--------|
SECTION

    echo "$issues_facets_response" | jq -r '
        .facets[]? | select(.property == "severities") | .values[]? |
        "| \(.val) | \(.count) |"
    ' >> "$report_file" 2>/dev/null || echo "| _Données non disponibles_ | - |" >> "$report_file"

    cat >> "$report_file" <<'SECTION'

### Par type

| Type | Nombre |
|------|--------|
SECTION

    echo "$issues_facets_response" | jq -r '
        .facets[]? | select(.property == "types") | .values[]? |
        "| \(.val) | \(.count) |"
    ' >> "$report_file" 2>/dev/null || echo "| _Données non disponibles_ | - |" >> "$report_file"

    # ── Issues par projet (résumé) ──
    cat >> "$report_file" <<'SECTION'

### Par projet

| Projet | Bugs | Vulnérabilités | Code Smells | Total |
|--------|------|----------------|-------------|-------|
SECTION

    for proj in "${PROJECTS[@]}"; do
        local proj_name
        proj_name=$(basename "$proj")
        echo "$all_issues" | jq -r --arg prefix "$proj/" '
            [.[] | select(.component | split(":")[1] | startswith($prefix))] |
            {
                bugs:  [.[] | select(.type == "BUG")] | length,
                vulns: [.[] | select(.type == "VULNERABILITY")] | length,
                smells:[.[] | select(.type == "CODE_SMELL")] | length
            } |
            "| **\($prefix | rtrimstr("/") | split("/") | last)** | \(.bugs) | \(.vulns) | \(.smells) | \(.bugs + .vulns + .smells) |"
        ' >> "$report_file" 2>/dev/null
    done

    # Tests projects (TEST_PROJECTS is derived from the solution above)
    for proj in "${TEST_PROJECTS[@]}"; do
        local proj_name
        proj_name=$(basename "$proj")
        echo "$all_issues" | jq -r --arg prefix "$proj/" '
            [.[] | select(.component | split(":")[1] | startswith($prefix))] |
            {
                bugs:  [.[] | select(.type == "BUG")] | length,
                vulns: [.[] | select(.type == "VULNERABILITY")] | length,
                smells:[.[] | select(.type == "CODE_SMELL")] | length
            } |
            "| \($prefix | rtrimstr("/") | split("/") | last) _(tests)_ | \(.bugs) | \(.vulns) | \(.smells) | \(.bugs + .vulns + .smells) |"
        ' >> "$report_file" 2>/dev/null
    done

    # ══════════════════════════════════════════════════════════════════════════
    # 6. Toutes les issues — Bugs
    # ══════════════════════════════════════════════════════════════════════════
    local bug_count
    bug_count=$(echo "$all_issues" | jq '[.[] | select(.type == "BUG")] | length')

    cat >> "$report_file" <<SECTION

---

## Tous les Bugs ($bug_count)

| # | Sévérité | Fichier | Ligne | Message |
|---|----------|---------|-------|---------|
SECTION

    echo "$all_issues" | jq -r '
        [.[] | select(.type == "BUG")] | sort_by(.severity | if . == "BLOCKER" then 0 elif . == "CRITICAL" then 1 elif . == "MAJOR" then 2 elif . == "MINOR" then 3 else 4 end) |
        to_entries[] |
        "| \(.key + 1) | \(.value.severity) | `\(.value.component | split(":") | last)` | \(.value.line // "-") | \(.value.message | gsub("\\|"; "∣") | gsub("\n"; " ") | .[0:150]) |"
    ' >> "$report_file" 2>/dev/null

    if [[ "$bug_count" -eq 0 ]]; then
        echo "| - | _Aucun bug_ | - | - | - |" >> "$report_file"
    fi

    # ══════════════════════════════════════════════════════════════════════════
    # 7. Toutes les issues — Vulnérabilités
    # ══════════════════════════════════════════════════════════════════════════
    local vuln_count
    vuln_count=$(echo "$all_issues" | jq '[.[] | select(.type == "VULNERABILITY")] | length')

    cat >> "$report_file" <<SECTION

---

## Toutes les Vulnérabilités ($vuln_count)

| # | Sévérité | Fichier | Ligne | Message |
|---|----------|---------|-------|---------|
SECTION

    echo "$all_issues" | jq -r '
        [.[] | select(.type == "VULNERABILITY")] | sort_by(.severity | if . == "BLOCKER" then 0 elif . == "CRITICAL" then 1 elif . == "MAJOR" then 2 elif . == "MINOR" then 3 else 4 end) |
        to_entries[] |
        "| \(.key + 1) | \(.value.severity) | `\(.value.component | split(":") | last)` | \(.value.line // "-") | \(.value.message | gsub("\\|"; "∣") | gsub("\n"; " ") | .[0:150]) |"
    ' >> "$report_file" 2>/dev/null

    if [[ "$vuln_count" -eq 0 ]]; then
        echo "| - | _Aucune vulnérabilité_ | - | - | - |" >> "$report_file"
    fi

    # ══════════════════════════════════════════════════════════════════════════
    # 8. Tous les Code Smells — par projet
    # ══════════════════════════════════════════════════════════════════════════
    local smell_count
    smell_count=$(echo "$all_issues" | jq '[.[] | select(.type == "CODE_SMELL")] | length')

    cat >> "$report_file" <<SECTION

---

## Tous les Code Smells ($smell_count)

SECTION

    local ALL_PROJ_PATHS=("${PROJECTS[@]}" "${TEST_PROJECTS[@]}")
    for proj in "${ALL_PROJ_PATHS[@]}"; do
        local proj_name
        proj_name=$(basename "$proj")
        local proj_smell_count
        proj_smell_count=$(echo "$all_issues" | jq --arg prefix "$proj/" '[.[] | select(.type == "CODE_SMELL") | select(.component | split(":")[1] | startswith($prefix))] | length')

        if [[ "$proj_smell_count" -eq 0 ]]; then
            continue
        fi

        cat >> "$report_file" <<SECTION

### ${proj_name} ($proj_smell_count code smells)

| # | Sévérité | Fichier | Ligne | Message | Règle |
|---|----------|---------|-------|---------|-------|
SECTION

        echo "$all_issues" | jq -r --arg prefix "$proj/" '
            [.[] | select(.type == "CODE_SMELL") | select(.component | split(":")[1] | startswith($prefix))] |
            sort_by(.severity | if . == "BLOCKER" then 0 elif . == "CRITICAL" then 1 elif . == "MAJOR" then 2 elif . == "MINOR" then 3 else 4 end) |
            to_entries[] |
            "| \(.key + 1) | \(.value.severity) | `\(.value.component | split(":") | last | split("/") | last)` | \(.value.line // "-") | \(.value.message | gsub("\\|"; "∣") | gsub("\n"; " ") | .[0:120]) | \(.value.rule // "-") |"
        ' >> "$report_file" 2>/dev/null
    done

    # ══════════════════════════════════════════════════════════════════════════
    # 9. Hotspots de sécurité
    # ══════════════════════════════════════════════════════════════════════════
    local hotspot_count
    hotspot_count=$(echo "$hotspots_response" | jq '.hotspots | length' 2>/dev/null || echo "0")

    cat >> "$report_file" <<SECTION

---

## Hotspots de sécurité ($hotspot_count)

### Par statut

| Statut | Nombre |
|--------|--------|
SECTION

    if [[ "$hotspot_count" -gt 0 ]]; then
        echo "$hotspots_response" | jq -r '
            [.hotspots[]?] | group_by(.status) | map({status: .[0].status, count: length}) |
            sort_by(.count) | reverse | .[]? |
            "| \(.status) | \(.count) |"
        ' >> "$report_file" 2>/dev/null
    else
        echo "| _Aucun hotspot_ | 0 |" >> "$report_file"
    fi

    if [[ "$hotspot_count" -gt 0 ]]; then
        cat >> "$report_file" <<'SECTION'

### Détail des hotspots

| # | Catégorie | Fichier | Ligne | Message |
|---|-----------|---------|-------|---------|
SECTION

        echo "$hotspots_response" | jq -r '
            [.hotspots[]?] | sort_by(.vulnerabilityProbability | if . == "HIGH" then 0 elif . == "MEDIUM" then 1 else 2 end) |
            to_entries[] |
            "| \(.key + 1) | \(.value.vulnerabilityProbability) | `\(.value.component | split(":") | last | split("/") | last)` | \(.value.line // "-") | \(.value.message | gsub("\\|"; "∣") | gsub("\n"; " ") | .[0:120]) |"
        ' >> "$report_file" 2>/dev/null
    fi

    # ══════════════════════════════════════════════════════════════════════════
    # Footer
    # ══════════════════════════════════════════════════════════════════════════
    cat >> "$report_file" <<FOOTER

---

_Rapport généré automatiquement par \`scripts/sonar-analyze.sh\` le $(date '+%Y-%m-%d à %H:%M:%S')_
_Total : $total_issues issues | Dashboard : ${SONAR_HOST}/dashboard?id=${pk}_
FOOTER

    log_success "Report generated: $report_file ($total_issues issues)"
}

# ══════════════════════════════════════════════════════════════════════════════
# Main flow
# ══════════════════════════════════════════════════════════════════════════════

main() {
    cd "$PROJECT_ROOT"

    echo ""
    echo "══════════════════════════════════════════════════════════════"
    echo "  SonarQube Analysis — Orkeon"
    echo "══════════════════════════════════════════════════════════════"
    echo "  Host    : $SONAR_HOST"
    echo "  Project : $SONAR_PROJECT_KEY"
    echo "══════════════════════════════════════════════════════════════"
    echo ""

    # Step 1: Check prerequisites
    check_prerequisites

    # Step 2: Handle sonar-project.properties (Bug #2)
    handle_sonar_properties

    # Step 3: Ensure SonarQube is running (Bug #1 — Docker overlay detection)
    ensure_sonarqube_running

    # Step 3b: Provision the transitional Quality Gate and bind it to the
    # project (R5.4 — idempotent; trajectory in docs/guides/quality-gate.md)
    provision_quality_gate

    # Step 4: Clean previous coverage results
    log_info "Cleaning previous coverage results..."
    rm -rf "$COVERAGE_DIR"
    mkdir -p "$COVERAGE_DIR"

    # Step 5: SonarScanner begin
    # Coverage is provided via sonar.coverageReportPaths (SonarQube generic format,
    # produced by reportgenerator from cobertura in Step 7b). The legacy
    # sonar.cs.opencover.reportsPaths was removed: nothing produced opencover.xml,
    # so it only emitted a WARN and forced a repo-wide glob scan.
    # sonar.qualitygate.wait=true makes the 'end' step wait for the Compute
    # Engine task and fail (non-zero exit) when the Quality Gate is in ERROR
    # (R5.4 — blocking gate).
    log_info "Starting SonarQube scanner..."
    # JS/TS analysis is disabled on purpose: this product is 100% C# (zero
    # first-party tsconfig.json outside examples/experiments/node_modules). The
    # SonarJS sensor otherwise auto-discovers ~1400 tsconfig.json in the vendored
    # projects under examples/others (cal.com, blocksuite, …) and builds a TS
    # "program" for each — analyzing 0 files (they're excluded) yet costing 1-2h+
    # of pure waste. Bogus file suffixes make the JS/TS sensors find no files and
    # skip entirely. Remove the two suffix lines below if first-party TS is added.
    dotnet sonarscanner begin \
        /k:"$SONAR_PROJECT_KEY" \
        /d:sonar.host.url="$SONAR_HOST" \
        /d:sonar.login="$SONAR_TOKEN" \
        /d:sonar.coverageReportPaths="coverage/merged/SonarQube.xml" \
        /d:sonar.qualitygate.wait=true \
        /d:sonar.qualitygate.timeout="$QUALITY_GATE_TIMEOUT" \
        /d:sonar.exclusions="**/bin/**,**/obj/**,examples/**,project/experiments/**,**/*.html,**/*.py" \
        /d:sonar.typescript.file.suffixes=".disabled-no-first-party-ts" \
        /d:sonar.javascript.file.suffixes=".disabled-no-first-party-js"

    # Step 6: Build
    log_info "Building solution..."
    dotnet build "$SOLUTION_PATH" --configuration Release

    # Step 7: Run tests with coverage
    # NOTE: Tests run in Debug mode — coverlet 8.x cannot instrument .NET 10 Release assemblies
    # (Release optimizations prevent instrumentation, producing empty coverage files)
    # The SonarScanner analysis still runs on the Release build from Step 6.
    log_info "Running tests with code coverage (Debug mode for coverlet instrumentation)..."
    dotnet test "$SOLUTION_PATH" \
        --collect:"XPlat Code Coverage" \
        --results-directory "$COVERAGE_DIR" \
        -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura \
    || log_warn "Some tests failed — continuing with analysis"

    # Step 7b: Convert Cobertura → SonarQube generic coverage format
    # (SonarQube 9.9 LTS does not support sonar.cs.cobertura.reportPaths)
    log_info "Converting coverage reports for SonarQube..."
    if command -v reportgenerator &>/dev/null; then
        reportgenerator \
            -reports:"$COVERAGE_DIR/**/coverage.cobertura.xml" \
            -targetdir:"$COVERAGE_DIR/merged" \
            -reporttypes:SonarQube 2>/dev/null \
        && log_info "Coverage conversion complete" \
        || log_warn "Coverage conversion failed — coverage may be incomplete"
    else
        log_warn "reportgenerator not installed — install with: dotnet tool install -g dotnet-reportgenerator-globaltool"
    fi

    # Step 8: SonarScanner end
    # WARNING: this phase is long and mostly silent on large solutions.
    # On the Orkeon repo (~60 MSBuild modules, ~400 source files), observed timings:
    #   - "Process project properties" : ~6-7 min (no output)
    #   - C# sensor + coverage import  : ~20-25 min (no output)
    #   - Upload + CE task             : ~2-5 min
    # Total: ~30-40 min. Do NOT interrupt — progress is made even when silent.
    # With sonar.qualitygate.wait=true this step also fails when the Quality
    # Gate is FAILED. The failure is captured (not fatal yet) so the Markdown
    # report is still generated; the script exits non-zero afterwards (R5.4).
    log_info "Finalizing SonarQube analysis..."
    log_warn "This step is silent for long stretches (~30-40 min total on this repo)."
    log_warn "Do not interrupt — the scanner is working even without stdout output."
    local scanner_end_status=0
    dotnet sonarscanner end /d:sonar.login="$SONAR_TOKEN" || scanner_end_status=$?
    if [[ $scanner_end_status -ne 0 ]]; then
        log_warn "sonarscanner end exited with code $scanner_end_status (Quality Gate FAILED or analysis error)."
        log_warn "The report is still generated below; the script will exit non-zero."
    fi

    # Step 9: Wait for background processing
    wait_for_ce_task

    # Step 10: Generate Markdown report
    generate_markdown_report

    # Step 11: Enforce the Quality Gate verdict (R5.4 — blocking gate)
    local gate_failed=0
    check_quality_gate || gate_failed=1

    # Summary
    echo ""
    echo "══════════════════════════════════════════════════════════════"
    log_success "Analysis complete!"
    echo "  Dashboard    : $SONAR_HOST/dashboard?id=$SONAR_PROJECT_KEY"
    echo "  Quality Gate : $QUALITY_GATE_STATUS"
    if [[ -d "$REPORT_DIR" ]]; then
        echo "  Report       : $(ls -t "$REPORT_DIR"/sonarqube-report-*.md 2>/dev/null | head -1)"
    fi
    echo "══════════════════════════════════════════════════════════════"
    echo ""

    if [[ $gate_failed -ne 0 ]]; then
        log_error "Quality Gate is $QUALITY_GATE_STATUS — failing the build (R5.4: blocking gate, see docs/guides/quality-gate.md)"
        exit 1
    fi
    if [[ $scanner_end_status -ne 0 ]]; then
        log_error "sonarscanner end failed with exit code $scanner_end_status — propagating failure"
        exit "$scanner_end_status"
    fi
}

main "$@"
