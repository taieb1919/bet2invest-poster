#!/usr/bin/env bash
# bmad-epic-runner.sh — Run bmad-pipeline.sh across all non-done stories of one or more epics
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Source bmad-pipeline.sh functions (guard prevents main execution)
BMAD_SOURCED=true
source "$SCRIPT_DIR/bmad-pipeline.sh"

# ─── Defaults ────────────────────────────────────────────────────────────────
EPIC_IDS=()
INTERACTIVE=false
STORY_MODE="auto"  # auto|full|dev|review
CONTINUE_ON_ERROR=false
DRY_RUN_FLAG=""
NO_COMMIT_FLAG=""
MAX_TURNS_FLAG=""
EXTRA_FLAGS=()

# ─── Usage ───────────────────────────────────────────────────────────────────
usage() {
    cat <<'EOF'
Usage: bmad-epic-runner.sh [OPTIONS] <epic-ids...>
       bmad-epic-runner.sh -i [OPTIONS]

Run bmad-pipeline.sh for all non-done stories of the specified epics.

ARGUMENTS:
  <epic-ids...>            One or more epic IDs (e.g., 7 8 9)

OPTIONS:
  --mode <full|dev|review> Force mode for all stories (default: auto-detect)
  --continue-on-error      Continue processing if a story fails
  --dry-run                Pass --dry-run to bmad-pipeline.sh
  --no-commit              Pass --no-commit to bmad-pipeline.sh
  --max-turns <n>          Pass --max-turns to bmad-pipeline.sh
  -i, --interactive        Interactive multi-select epic menu
  -h, --help               Show this help

AUTO-DETECT MODE:
  If a story file exists in .bmadOutput/implementation-artifacts/ → --dev
  Otherwise → --full --epic <id>

EXAMPLES:
  ./bmad-epic-runner.sh 7 8 9                    # Process epics 7, 8, 9
  ./bmad-epic-runner.sh --dry-run 7              # Dry-run epic 7
  ./bmad-epic-runner.sh -i --dry-run             # Interactive + dry-run
  ./bmad-epic-runner.sh --mode dev 7             # Force dev mode for all
  ./bmad-epic-runner.sh --continue-on-error 7 8  # Don't stop on failure
EOF
    exit 0
}

# ─── Parse Arguments ─────────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
    case "$1" in
        -i|--interactive|i-) INTERACTIVE=true; shift ;;
        --mode)              STORY_MODE="$2"; shift 2 ;;
        --continue-on-error) CONTINUE_ON_ERROR=true; shift ;;
        --dry-run)           DRY_RUN_FLAG="--dry-run"; EXTRA_FLAGS+=(--dry-run); shift ;;
        --no-commit)         NO_COMMIT_FLAG="--no-commit"; EXTRA_FLAGS+=(--no-commit); shift ;;
        --max-turns)         MAX_TURNS_FLAG="--max-turns $2"; EXTRA_FLAGS+=(--max-turns "$2"); shift 2 ;;
        -h|--help)           usage ;;
        -*)                  echo "Option inconnue: $1"; exit 1 ;;
        *)
            # Support comma-separated IDs: "7,8,9" -> 7 8 9
            IFS=',' read -ra _ids <<< "$1"
            for _id in "${_ids[@]}"; do
                if [[ ! "$_id" =~ ^[0-9.]+$ ]]; then
                    echo "Erreur: '$_id' n'est pas un epic ID valide (attendu: numérique)."
                    exit 1
                fi
                EPIC_IDS+=("$_id")
            done
            shift
            ;;
    esac
done

# ─── Interactive Epic Selection ──────────────────────────────────────────────
if [[ "$INTERACTIVE" == "true" ]]; then
    mapfile -t epic_items < <(list_epics)
    if [[ ${#epic_items[@]} -eq 0 ]]; then
        echo -e "${RED}Aucun epic non-done trouvé.${NC}"
        exit 1
    fi

    echo ""
    echo -e "${BOLD}${CYAN}═══ Epics disponibles ═══${NC}"
    local_idx=1
    for item in "${epic_items[@]}"; do
        local_id="${item%%|*}"
        local_label="${item#*|}"
        echo -e "  ${BOLD}${local_idx})${NC} Epic ${local_id}: ${local_label}"
        ((local_idx++)) || true
    done
    echo ""
    echo -e "  Entrez les numéros ou IDs séparés par des espaces (ex: ${BOLD}1 3 5${NC} ou ${BOLD}7 8 9${NC})"
    read -rp "  Sélection: " selections

    for sel in $selections; do
        if _resolve_choice "$sel" "${epic_items[@]}"; then
            EPIC_IDS+=("$MENU_RESULT_ID")
        else
            echo -e "${YELLOW}Sélection ignorée: $sel${NC}"
        fi
    done
fi

if [[ ${#EPIC_IDS[@]} -eq 0 ]]; then
    echo "Erreur: aucun epic spécifié. Utilisez -i pour le mode interactif ou passez des IDs."
    exit 1
fi

# ─── Determine mode for a story ─────────────────────────────────────────────
# Fallback: extract stories from sprint-status.yaml when epics.md has no entries
# Args: epic_id
# Output: lines of "ID|title [status]"
list_stories_from_sprint_status() {
    local epic_id="$1"
    if [[ ! -f "$SPRINT_STATUS_FILE" ]]; then
        return
    fi
    # Match lines like "  7-1-commandes-start-et-stop: backlog" under epic-7
    local in_epic=false
    while IFS= read -r line; do
        # Detect epic header
        if [[ "$line" =~ ^[[:space:]]*epic-([0-9.]+): ]]; then
            if [[ "${BASH_REMATCH[1]}" == "$epic_id" ]]; then
                in_epic=true
            else
                $in_epic && break
            fi
            continue
        fi
        # Skip retrospective lines
        [[ "$line" == *retrospective* ]] && continue
        # Parse story line: "  7-1-some-slug: status"
        if $in_epic && [[ "$line" =~ ^[[:space:]]+([0-9]+-[0-9]+)-([^:]+):[[:space:]]*(.+)$ ]]; then
            local raw_id="${BASH_REMATCH[1]}"
            local slug="${BASH_REMATCH[2]}"
            local status="${BASH_REMATCH[3]// /}"
            # Convert 7-1 -> 7.1
            local story_id="${raw_id//-/.}"
            [[ "$status" == "done" ]] && continue
            # Humanize slug: replace dashes with spaces
            local title="${slug//-/ }"
            echo "${story_id}|${title} [${status}]"
        fi
    done < "$SPRINT_STATUS_FILE"
}

determine_mode() {
    local story_id="$1"
    local epic_id="$2"

    if [[ "$STORY_MODE" != "auto" ]]; then
        echo "$STORY_MODE"
        return
    fi

    # Check if story file already exists
    local pattern="$PROJECT_ROOT/.bmadOutput/implementation-artifacts/${story_id}*.md"
    if compgen -G "$pattern" > /dev/null 2>&1; then
        echo "dev"
    else
        echo "full"
    fi
}

# ─── Run Pipeline ────────────────────────────────────────────────────────────
declare -A RESULTS=()
TOTAL=0
SUCCESS=0
FAILED=0
SKIPPED=0

echo ""
echo -e "${BOLD}${CYAN}══════════════════════════════════════════════════════════════${NC}"
echo -e "${BOLD}${CYAN}  BMAD Epic Runner — Epics: ${EPIC_IDS[*]}${NC}"
echo -e "${BOLD}${CYAN}══════════════════════════════════════════════════════════════${NC}"
echo ""

for epic_id in "${EPIC_IDS[@]}"; do
    echo -e "${BOLD}${BLUE}── Epic ${epic_id} ──${NC}"

    mapfile -t stories < <(list_stories_for_epic "$epic_id")

    # Fallback: if epics.md has no stories, try sprint-status.yaml
    if [[ ${#stories[@]} -eq 0 ]]; then
        mapfile -t stories < <(list_stories_from_sprint_status "$epic_id")
    fi

    epic_story_count=0
    epic_story_failed=0

    if [[ ${#stories[@]} -eq 0 ]]; then
        echo -e "  ${YELLOW}Aucune story trouvée pour l'epic ${epic_id}.${NC}"
        echo ""
        continue
    fi

    for story_line in "${stories[@]}"; do
        [[ -z "$story_line" ]] && continue
        story_id="${story_line%%|*}"
        story_label="${story_line#*|}"
        ((TOTAL++)) || true
        ((epic_story_count++)) || true

        mode=$(determine_mode "$story_id" "$epic_id")

        # Build command
        cmd=("$SCRIPT_DIR/bmad-pipeline.sh" --story "$story_id")
        case "$mode" in
            full)   cmd+=(--full --epic "$epic_id") ;;
            dev)    cmd+=(--dev) ;;
            review) cmd+=(--review) ;;
        esac
        cmd+=("${EXTRA_FLAGS[@]}")

        echo -e "  ${BOLD}→ Story ${story_id}${NC}: ${story_label} ${DIM}[mode: ${mode}]${NC}"
        echo -e "    ${DIM}$ ${cmd[*]}${NC}"

        if [[ -n "$DRY_RUN_FLAG" ]]; then
            RESULTS["$story_id"]="dry-run"
            ((SUCCESS++)) || true
            continue
        fi

        if "${cmd[@]}"; then
            RESULTS["$story_id"]="ok"
            ((SUCCESS++)) || true
        else
            RESULTS["$story_id"]="FAILED"
            ((FAILED++)) || true
            ((epic_story_failed++)) || true
            if [[ "$CONTINUE_ON_ERROR" == "false" ]]; then
                echo -e "  ${RED}Story ${story_id} a échoué. Arrêt (utilisez --continue-on-error pour continuer).${NC}"
                break 2
            fi
        fi
        echo ""
    done

    # ─── Retrospective for this epic (only if all stories succeeded) ────
    if [[ $epic_story_failed -gt 0 ]]; then
        echo -e "  ${YELLOW}Rétrospective Epic ${epic_id} ignorée — ${epic_story_failed} story(s) en échec.${NC}"
        echo ""
        continue
    fi

    echo -e "  ${BOLD}${CYAN}→ Rétrospective Epic ${epic_id}${NC}"
    retro_prompt="/bmad-bmm-retrospective"
    retro_cmd=(
        claude -p
        --model sonnet
        --permission-mode acceptEdits
        --allowedTools "Read,Write,Edit,Glob,Grep,Task,Bash"
        --no-session-persistence
    )
    if claude --help 2>/dev/null | grep -q -- '--max-turns'; then
        retro_cmd+=(--max-turns "$MAX_TURNS")
    fi
    echo -e "    ${DIM}$ ${retro_cmd[*]}${NC}"
    echo -e "    ${DIM}[prompt: ${retro_prompt}]${NC}"

    if [[ -n "$DRY_RUN_FLAG" ]]; then
        RESULTS["retro-${epic_id}"]="dry-run"
    else
        if (unset CLAUDECODE; "${retro_cmd[@]}" <<< "$retro_prompt") 2>&1; then
            RESULTS["retro-${epic_id}"]="ok"
        else
            RESULTS["retro-${epic_id}"]="FAILED"
            ((FAILED++)) || true
            if [[ "$CONTINUE_ON_ERROR" == "false" ]]; then
                echo -e "  ${RED}Rétrospective Epic ${epic_id} a échoué. Arrêt.${NC}"
                break
            fi
        fi
    fi
    echo ""
done

# ─── Final Report ────────────────────────────────────────────────────────────
echo ""
echo -e "${BOLD}${CYAN}══════════════════════════════════════════════════════════════${NC}"
echo -e "${BOLD}${CYAN}  Rapport Final${NC}"
echo -e "${BOLD}${CYAN}══════════════════════════════════════════════════════════════${NC}"
echo ""
echo -e "  Total: ${TOTAL}  |  ${GREEN}OK: ${SUCCESS}${NC}  |  ${RED}Échecs: ${FAILED}${NC}"
echo ""

for story_id in $(printf '%s\n' "${!RESULTS[@]}" | sort -V); do
    status="${RESULTS[$story_id]}"
    case "$status" in
        ok)      color="$GREEN" ;;
        dry-run) color="$YELLOW" ;;
        *)       color="$RED" ;;
    esac
    echo -e "  Story ${BOLD}${story_id}${NC}: ${color}${status}${NC}"
done
echo ""

[[ $FAILED -gt 0 ]] && exit 1
exit 0
