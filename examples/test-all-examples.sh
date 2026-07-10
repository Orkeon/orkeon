#!/usr/bin/env bash
# test-all-examples.sh
# Automated test runner for all 101 Orkeon examples
# Uses Docker Desktop Models (ai/granite-4.0-h-tiny) via localhost:12434
#
# Usage:
#   ./examples/test-all-examples.sh                        # Test all (load level)
#   ./examples/test-all-examples.sh --level build          # Build only
#   ./examples/test-all-examples.sh --level load           # Config loading (default)
#   ./examples/test-all-examples.sh --level run            # Full crew execution
#   ./examples/test-all-examples.sh --category 01          # Single category
#   ./examples/test-all-examples.sh --example 01-enterprise/01-research-assistant
#   ./examples/test-all-examples.sh --timeout 300          # Custom timeout
#   ./examples/test-all-examples.sh --settings examples/appsettings/appsettings.json
#   ./examples/test-all-examples.sh --stop-on-error        # Stop at first failure
set -uo pipefail

# ============================================================
# Parse arguments
# ============================================================
LEVEL="load"
TIMEOUT=180
CATEGORY=""
EXAMPLE=""
SETTINGS=""
STOP_ON_ERROR=false
VERBOSE=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --level)     LEVEL="$2"; shift 2 ;;
        --timeout)   TIMEOUT="$2"; shift 2 ;;
        --category)  CATEGORY="$2"; shift 2 ;;
        --example)   EXAMPLE="$2"; shift 2 ;;
        --settings)  SETTINGS="$2"; shift 2 ;;
        --stop-on-error) STOP_ON_ERROR=true; shift ;;
        --verbose)   VERBOSE=true; shift ;;
        *) echo "Unknown option: $1"; exit 1 ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(dirname "$SCRIPT_DIR")"
REPORT_DIR="$SCRIPT_DIR/test-reports"
TIMESTAMP=$(date +"%Y-%m-%d_%H%M%S")
REPORT_PATH="$REPORT_DIR/test-report-$TIMESTAMP.md"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
NC='\033[0m'

# ============================================================
# Step 0: Build
# ============================================================
echo -e "\n${CYAN}=== Building Orkeon.Examples.sln ===${NC}"
if ! dotnet build "$SCRIPT_DIR/Orkeon.Examples.sln" --configuration Release --verbosity quiet 2>&1; then
    echo -e "${RED}BUILD FAILED${NC}"
    exit 1
fi
echo -e "${GREEN}Build OK${NC}"

if [ "$LEVEL" = "build" ]; then
    echo -e "\n${GREEN}=== Build-only mode: done ===${NC}"
    exit 0
fi

# ============================================================
# Step 1: Discover examples
# ============================================================
declare -a EXAMPLES

if [ -n "$EXAMPLE" ]; then
    config_path="$SCRIPT_DIR/$EXAMPLE/config.yaml"
    if [ ! -f "$config_path" ]; then
        echo -e "${RED}ERROR: config.yaml not found at $config_path${NC}"
        exit 1
    fi
    EXAMPLES+=("$EXAMPLE")
else
    while IFS= read -r line; do
        rel="${line#$SCRIPT_DIR/}"
        rel="$(dirname "$rel")"
        if [ -z "$CATEGORY" ] || [[ "$rel" == ${CATEGORY}* ]]; then
            EXAMPLES+=("$rel")
        fi
    done < <(find "$SCRIPT_DIR" -name "config.yaml" \
        -not -path "*/_legacy/*" \
        -not -path "*/_shared/*" \
        -not -path "*/runners/*" \
        -not -path "*/.vs/*" | sort)
fi

TOTAL=${#EXAMPLES[@]}
echo -e "\n${CYAN}=== Testing $TOTAL examples (level: $LEVEL, timeout: ${TIMEOUT}s) ===${NC}\n"

# ============================================================
# Step 2: Run tests
# ============================================================
PASSED=0
FAILED=0
INDEX=0

# Arrays for report
declare -a R_EXAMPLES R_STATUS R_DURATION R_WARNINGS R_ERROR

for example in "${EXAMPLES[@]}"; do
    INDEX=$((INDEX + 1))
    config_path="$SCRIPT_DIR/$example/config.yaml"

    # Determine runner
    cat_dir="${example%%/*}"
    if [ "$cat_dir" = "03-finance-trading" ]; then
        runner="$SCRIPT_DIR/runners/trading"
    else
        runner="$SCRIPT_DIR/runners/standard"
    fi

    printf "[%d/%d] %-60s " "$INDEX" "$TOTAL" "$example"

    # Build settings args
    settings_args=""
    if [ -n "$SETTINGS" ]; then
        settings_args="--settings $SETTINGS"
    fi

    # Run with timeout
    start_time=$(date +%s%N)
    tmpfile=$(mktemp)

    set +e
    timeout "${TIMEOUT}s" dotnet run --project "$runner" --no-build --configuration Release \
        -- --config "$config_path" $settings_args > "$tmpfile" 2>&1
    exit_code=$?
    set -e

    end_time=$(date +%s%N)
    duration_ms=$(( (end_time - start_time) / 1000000 ))
    duration_s=$(echo "scale=1; $duration_ms / 1000" | bc 2>/dev/null || echo "${duration_ms}ms")

    output=$(cat "$tmpfile")
    rm -f "$tmpfile"

    # Count warnings
    warn_count=$(echo "$output" | grep -c "warn:" 2>/dev/null || echo 0)

    # Determine status
    status="UNKNOWN"
    error_msg=""

    if [ $exit_code -eq 124 ]; then
        # timeout killed it
        status="TIMEOUT"
        error_msg="Killed after ${TIMEOUT}s"
        FAILED=$((FAILED + 1))
    elif [ $exit_code -eq 0 ]; then
        status="PASS"
        PASSED=$((PASSED + 1))
    elif echo "$output" | grep -q "Successfully created crew"; then
        # Crew loaded but execution failed
        if [ "$LEVEL" = "load" ]; then
            status="PASS"
            PASSED=$((PASSED + 1))
        else
            status="FAIL"
            error_msg=$(echo "$output" | grep -E "fail:|Exception|Error" | head -3 | tr '\n' '; ')
            FAILED=$((FAILED + 1))
        fi
    else
        status="FAIL"
        error_msg=$(echo "$output" | grep -E "fail:|Exception|Error|ERROR" | head -3 | tr '\n' '; ')
        FAILED=$((FAILED + 1))
    fi

    # Color output
    case "$status" in
        PASS)    color="$GREEN" ;;
        FAIL)    color="$RED" ;;
        TIMEOUT) color="$YELLOW" ;;
        *)       color="$MAGENTA" ;;
    esac

    echo -e "${color}${status}${NC} (${duration_s}s)"

    if [ "$VERBOSE" = true ] && [ "$warn_count" -gt 0 ]; then
        echo "$output" | grep "warn:" | while read -r w; do
            echo -e "  ${YELLOW}WARN: $w${NC}"
        done
    fi

    if [ -n "$error_msg" ] && [ "$status" != "PASS" ]; then
        echo -e "  ${RED}${error_msg:0:120}${NC}"
    fi

    # Store for report
    R_EXAMPLES+=("$example")
    R_STATUS+=("$status")
    R_DURATION+=("$duration_s")
    R_WARNINGS+=("$warn_count")
    R_ERROR+=("$error_msg")

    if [ "$STOP_ON_ERROR" = true ] && [ "$status" != "PASS" ]; then
        echo -e "\n${YELLOW}Stopping on first error (--stop-on-error)${NC}"
        break
    fi
done

# ============================================================
# Step 3: Summary
# ============================================================
echo ""
echo -e "${CYAN}$(printf '=%.0s' {1..60})${NC}"
if [ "$FAILED" -eq 0 ]; then
    echo -e "${GREEN}RESULTS: $PASSED passed, $FAILED failed / $TOTAL total${NC}"
else
    echo -e "${RED}RESULTS: $PASSED passed, $FAILED failed / $TOTAL total${NC}"
fi
echo -e "${CYAN}$(printf '=%.0s' {1..60})${NC}"

# ============================================================
# Step 4: Generate Markdown report
# ============================================================
mkdir -p "$REPORT_DIR"

cat > "$REPORT_PATH" << HEADER
# Orkeon Examples Test Report

- **Date**: $(date "+%Y-%m-%d %H:%M:%S")
- **Level**: \`$LEVEL\`
- **Model**: \`ai/granite-4.0-h-tiny\` (Docker Desktop Models)
- **Timeout**: ${TIMEOUT}s per example
- **Results**: $PASSED passed, $FAILED failed / $TOTAL total

## Detailed Results

| # | Example | Status | Duration | Warnings | Error |
|---|---------|--------|----------|----------|-------|
HEADER

for i in $(seq 0 $((${#R_EXAMPLES[@]} - 1))); do
    err="${R_ERROR[$i]}"
    err="${err:0:80}"
    err="${err//|//}"  # escape pipes
    echo "| $((i+1)) | \`${R_EXAMPLES[$i]}\` | ${R_STATUS[$i]} | ${R_DURATION[$i]}s | ${R_WARNINGS[$i]} | $err |" >> "$REPORT_PATH"
done

echo -e "\nReport saved to: ${CYAN}$REPORT_PATH${NC}"

# Exit code
if [ "$FAILED" -gt 0 ]; then exit 1; else exit 0; fi
