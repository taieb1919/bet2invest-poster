#!/usr/bin/env bash
# bmad-pipeline.sh — Headless BMAD Pipeline for bet2invest-poster
# Chains create-story -> dev-story -> code-review -> fix+commit phases
# using Claude Code CLI with model switching per phase.
set -euo pipefail

# ─── Constants ──────────────────────────────────────────────────────────────
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$SCRIPT_DIR"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
LOGS_DIR="$PROJECT_ROOT/logs/bmad-pipeline/$TIMESTAMP"

# Default models per phase
CREATE_MODEL="opus"
DEV_MODEL="sonnet"
REVIEW_MODEL="opus"
FIX_MODEL="sonnet"

# Defaults
MAX_TURNS=50
DRY_RUN=false
NO_COMMIT=false
VERBOSE=true
STORY_ID=""
EPIC_ID=""
MODE=""
SINGLE_PHASE=""

# Epics file for interactive mode
EPICS_FILE="$PROJECT_ROOT/.bmadOutput/planning-artifacts/epics.md"
SPRINT_STATUS_FILE="$PROJECT_ROOT/.bmadOutput/implementation-artifacts/sprint-status.yaml"

# AllowedTools per phase
TOOLS_CREATE="Read,Write,Glob,Grep,Task,Bash"
TOOLS_DEV="Read,Write,Edit,Glob,Grep,Task,Bash"
TOOLS_REVIEW="Read,Write,Edit,Glob,Grep,Task,Bash"
TOOLS_FIX="Read,Write,Edit,Glob,Grep,Task,Bash"

# ─── Colors ──────────────────────────────────────────────────────────────────
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
BOLD='\033[1m'
DIM='\033[2m'
NC='\033[0m'

# ─── Phase tracking ─────────────────────────────────────────────────────────
declare -A PHASE_STATUS=()
declare -A PHASE_DURATION=()
declare -A PHASE_START_TIME=()

# ─── Functions ───────────────────────────────────────────────────────────────

usage() {
    cat <<'EOF'
Usage: bmad-pipeline.sh [--story <id> [MODE]] [OPTIONS]
       bmad-pipeline.sh [-i | --interactive] [OPTIONS]
       bmad-pipeline.sh                        (interactive mode)

INTERACTIVE MODE:
  (no arguments)        Launch interactive menu
  -i, --interactive     Launch interactive menu (explicit)

MODES (mutually exclusive, CLI mode):
  --full              create -> dev -> review -> fix -> commit (requires --epic)
  --dev               dev -> review -> fix -> commit (story already created)
  --review            review -> fix -> commit
  --phase <name>      Single phase: create-story|dev-story|code-review|fix-commit

REQUIRED (CLI mode):
  --story <id>        Story ID (e.g., 12.6, 13.1)
  --epic <id>         Epic ID (required for create-story phase)

MODEL OVERRIDES:
  --create-model <m>  Model for create-story  (default: opus)
  --dev-model <m>     Model for dev-story     (default: sonnet)
  --review-model <m>  Model for code-review   (default: opus)
  --fix-model <m>     Model for fix+commit    (default: sonnet)

OPTIONS:
  --max-turns <n>     Max turns per phase     (default: 50)
  --dry-run           Show commands without executing
  --no-commit         Skip final git commit
  --verbose           Show real-time output
  -h, --help          Show this help

EXAMPLES:
  ./bmad-pipeline.sh                                  # Interactive mode
  ./bmad-pipeline.sh -i --dry-run                     # Interactive + dry-run
  ./bmad-pipeline.sh --story 13.1 --full --epic 13    # CLI mode
  ./bmad-pipeline.sh --story 12.6 --dev
  ./bmad-pipeline.sh --story 12.6 --review
  ./bmad-pipeline.sh --story 12.6 --phase dev-story
  ./bmad-pipeline.sh --story 12.6 --dev --dry-run
EOF
    exit 0
}

log_info()  { echo -e "${BLUE}[INFO]${NC}  $*"; }
log_ok()    { echo -e "${GREEN}[OK]${NC}    $*"; }
log_warn()  { echo -e "${YELLOW}[WARN]${NC}  $*"; }
log_error() { echo -e "${RED}[ERROR]${NC} $*"; }
log_phase() { echo -e "\n${BOLD}${CYAN}━━━ Phase: $1 ━━━${NC}"; }
die()       { log_error "$@"; exit 1; }

# Get all changed files (working tree + staged + untracked)
get_changed_files() {
    local baseline="${1:-HEAD}"
    {
        git diff --name-only "$baseline" 2>/dev/null || true
        git diff --name-only --cached 2>/dev/null || true
        git ls-files --others --exclude-standard 2>/dev/null || true
    } | sort -u | grep -v '^$' || true
}

# ─── Prompt Builders ─────────────────────────────────────────────────────────

prompt_create_story() {
    cat <<PROMPT
**LANGUE**: Communique TOUJOURS en FRANCAIS. Tous les documents generes doivent etre en francais.

**MODE**: #yolo - Skip ALL confirmations. Simule un expert user pour toutes les decisions. Produis le workflow automatiquement du debut a la fin.

Tu executes le workflow BMAD "create-story" en mode headless.

ETAPES CRITIQUES:
1. Lis le fichier complet: ${PROJECT_ROOT}/_bmad/core/tasks/workflow.xml
2. C'est le moteur d'execution. Le workflow-config est: ${PROJECT_ROOT}/_bmad/bmm/workflows/4-implementation/create-story/workflow.yaml
3. Passe ce chemin yaml comme parametre 'workflow-config' aux instructions du workflow.xml
4. Suis les instructions du workflow.xml EXACTEMENT pour traiter le workflow config
5. Sauvegarde les outputs apres CHAQUE section de template-output

CONTEXTE:
- Story ID: ${STORY_ID}
- Epic ID: ${EPIC_ID}
- Le fichier epics est dans: ${PROJECT_ROOT}/.bmadOutput/planning-artifacts/epics.md
- Cherche l'epic ${EPIC_ID} et cree la story ${STORY_ID}
- Sauvegarde dans: ${PROJECT_ROOT}/.bmadOutput/implementation-artifacts/

IMPORTANT: Mode #yolo active. Ne pose AUCUNE question, ne demande AUCUNE confirmation. Simule toutes les reponses utilisateur comme un expert technique senior.
PROMPT
}

prompt_dev_story() {
    cat <<PROMPT
**LANGUE**: Communique TOUJOURS en FRANCAIS. Tous les documents generes doivent etre en francais.

**MODE**: #yolo - Skip ALL confirmations. Implemente TOUT automatiquement sans jamais attendre une reponse utilisateur.

Tu executes le workflow BMAD "dev-story" en mode headless.

ETAPES CRITIQUES:
1. Lis le fichier complet: ${PROJECT_ROOT}/_bmad/core/tasks/workflow.xml
2. C'est le moteur d'execution. Le workflow-config est: ${PROJECT_ROOT}/_bmad/bmm/workflows/4-implementation/dev-story/workflow.yaml
3. Passe ce chemin yaml comme parametre 'workflow-config' aux instructions du workflow.xml
4. Suis les instructions du workflow.xml EXACTEMENT
5. Sauvegarde les outputs apres CHAQUE section

CONTEXTE:
- Story ID: ${STORY_ID}
- Trouve le fichier story dans: ${PROJECT_ROOT}/.bmadOutput/implementation-artifacts/ (pattern: ${STORY_ID}*.md)
- Implemente TOUTES les taches et sous-taches de la story

GATE DE VALIDATION (obligatoire avant de terminer):
- \`dotnet build src/Bet2InvestPoster\` doit passer
- \`dotnet test tests/Bet2InvestPoster.Tests\` doit passer

IMPORTANT: Mode #yolo active. Ne pose AUCUNE question. Implemente tout de A a Z.
PROMPT
}

prompt_code_review() {
    local changed_files="$1"
    cat <<PROMPT
**LANGUE**: Communique TOUJOURS en FRANCAIS. Tous les documents generes doivent etre en francais.

**MODE**: #yolo - Skip ALL confirmations. Review ADVERSARIAL automatique.

Tu executes le workflow BMAD "code-review" en mode headless adversarial.

ETAPES CRITIQUES:
1. Lis le fichier complet: ${PROJECT_ROOT}/_bmad/core/tasks/workflow.xml
2. C'est le moteur d'execution. Le workflow-config est: ${PROJECT_ROOT}/_bmad/bmm/workflows/4-implementation/code-review/workflow.yaml
3. Passe ce chemin yaml comme parametre 'workflow-config' aux instructions du workflow.xml
4. Suis les instructions du workflow.xml EXACTEMENT

CONTEXTE:
- Story ID: ${STORY_ID}
- Trouve le fichier story dans: ${PROJECT_ROOT}/.bmadOutput/implementation-artifacts/ (pattern: ${STORY_ID}*.md)

FICHIERS MODIFIES A REVIEWER:
${changed_files}

CONSIGNES ADVERSARIALES:
- Trouve MINIMUM 3 problemes concrets (objectif: 3-10)
- Challenge: qualite code, couverture tests, conformite architecture, securite, performance
- JAMAIS "looks good" - tu DOIS trouver des issues
- Pour chaque issue: fichier, ligne, severite (Critical/Major/Minor), description, fix suggere

IMPORTANT: Mode #yolo active. Produis le rapport complet sans interruption.
PROMPT
}

prompt_fix_commit() {
    local changed_files="$1"
    local review_log="$2"
    local review_content=""

    if [[ -n "$review_log" && -f "$review_log" ]]; then
        review_content=$(tail -200 "$review_log" 2>/dev/null || echo "(Erreur lecture rapport)")
    else
        review_content="(Pas de rapport disponible - utilise git diff pour identifier les ameliorations)"
    fi

    local commit_instruction
    if [[ "$NO_COMMIT" == "true" ]]; then
        commit_instruction="NE PAS committer (--no-commit actif). Arrete-toi apres les corrections et tests."
    else
        commit_instruction="Cree un commit conventionnel: feat|fix(scope): description de la story ${STORY_ID}. Ne JAMAIS ajouter de ligne Co-Authored-By."
    fi

    cat <<PROMPT
**LANGUE**: Communique TOUJOURS en FRANCAIS.

Tu es le developpeur BMAD. Mission: corriger les issues du code review pour la story ${STORY_ID} et preparer le commit.

RAPPORT DE CODE REVIEW:
${review_content}

FICHIERS MODIFIES:
${changed_files}

ETAPES:
1. Lis le rapport de review ci-dessus attentivement
2. Corrige TOUTES les issues identifiees — Critical, Major ET Minor/Low inclus
3. Verifie la compilation: \`dotnet build src/Bet2InvestPoster\`
4. Lance les tests: \`dotnet test tests/Bet2InvestPoster.Tests\`
5. Met a jour le sprint-status: dans ${PROJECT_ROOT}/.bmadOutput/implementation-artifacts/sprint-status.yaml, change le statut de la story ${STORY_ID} (prefix: ${STORY_ID%.*}-${STORY_ID##*.}) pour refleter son avancement (ex: backlog→in-progress, in-progress→review, review→done selon la phase completee)
6. Si tout passe, ${commit_instruction}

REGLES DE COMMIT:
- Format: type(scope): description
- Le scope correspond au domaine principal modifie
- Un seul commit pour l'ensemble des corrections
- Inclure le fichier sprint-status.yaml modifie dans le commit
PROMPT
}

# ─── Phase Execution ─────────────────────────────────────────────────────────

run_phase() {
    local phase="$1"
    local model="$2"
    local tools="$3"
    local prompt="$4"

    local phase_log="$LOGS_DIR/${phase}.log"

    log_phase "$phase (model: $model)"

    # Build claude command
    local -a cmd=(
        claude
        -p
        --model "$model"
        --allowedTools "$tools"
        --no-session-persistence
    )

    # Add --max-turns if the CLI supports it
    if claude --help 2>/dev/null | grep -q -- '--max-turns'; then
        cmd+=(--max-turns "$MAX_TURNS")
    fi

    if [[ "$DRY_RUN" == "true" ]]; then
        log_warn "DRY RUN — commande:"
        echo -e "${DIM}  ${cmd[*]}${NC}"
        echo -e "${DIM}  [prompt: ${#prompt} chars]${NC}"
        echo ""
        echo "$prompt" > "$phase_log"
        log_info "Prompt sauvegarde: $phase_log"
        PHASE_STATUS[$phase]="dry-run"
        PHASE_DURATION[$phase]="0s"
        return 0
    fi

    # Record start
    PHASE_START_TIME[$phase]=$(date +%s)
    PHASE_STATUS[$phase]="running"

    log_info "Demarrage a $(date +%H:%M:%S)..."
    log_info "Log: $phase_log"

    local exit_code=0

    # Execute claude in a subshell to unset CLAUDECODE (avoid nested session error)
    if [[ "$VERBOSE" == "true" ]]; then
        (unset CLAUDECODE; "${cmd[@]}" <<< "$prompt") 2>&1 | tee "$phase_log" || exit_code=$?
    else
        (unset CLAUDECODE; "${cmd[@]}" <<< "$prompt") > "$phase_log" 2>&1 || exit_code=$?
    fi

    # Record duration
    local end_time
    end_time=$(date +%s)
    local duration=$(( end_time - PHASE_START_TIME[$phase] ))
    local minutes=$(( duration / 60 ))
    local seconds=$(( duration % 60 ))
    PHASE_DURATION[$phase]="${minutes}m${seconds}s"

    if [[ $exit_code -eq 0 ]]; then
        PHASE_STATUS[$phase]="success"
        log_ok "Phase $phase terminee en ${minutes}m${seconds}s"
    else
        PHASE_STATUS[$phase]="failed (exit $exit_code)"
        log_error "Phase $phase echouee (exit $exit_code) apres ${minutes}m${seconds}s"
        log_error "Voir: $phase_log"
        return $exit_code
    fi
}

# ─── Summary Report ──────────────────────────────────────────────────────────

print_summary() {
    echo ""
    echo -e "${BOLD}${CYAN}================================================================${NC}"
    echo -e "${BOLD}  BMAD Pipeline — Rapport Final${NC}"
    echo -e "${BOLD}${CYAN}================================================================${NC}"
    echo -e "  Story:     ${BOLD}${STORY_ID}${NC}"
    if [[ -n "$EPIC_ID" ]]; then
        echo -e "  Epic:      ${BOLD}${EPIC_ID}${NC}"
    fi
    echo -e "  Mode:      ${MODE}"
    echo -e "  Timestamp: ${TIMESTAMP}"
    echo -e "  Logs:      ${LOGS_DIR}"
    echo ""

    local all_success=true
    for phase in "${PHASES[@]}"; do
        local status="${PHASE_STATUS[$phase]:-skipped}"
        local duration="${PHASE_DURATION[$phase]:-—}"
        local icon
        case "$status" in
            success)   icon="${GREEN}+${NC}" ;;
            dry-run)   icon="${YELLOW}o${NC}" ;;
            failed*)   icon="${RED}x${NC}" ; all_success=false ;;
            *)         icon="${DIM}-${NC}" ;;
        esac
        printf "  %b  %-15s  %-10s  %s\n" "$icon" "$phase" "$duration" "$status"
    done

    echo -e "${BOLD}${CYAN}================================================================${NC}"

    if [[ "$all_success" == "true" && "$DRY_RUN" != "true" ]]; then
        echo -e "  ${GREEN}Pipeline termine avec succes${NC}"
    elif [[ "$DRY_RUN" == "true" ]]; then
        echo -e "  ${YELLOW}Dry run termine — aucune execution reelle${NC}"
    else
        echo -e "  ${RED}Pipeline termine avec des erreurs${NC}"
    fi
    echo ""
}

# ─── Interactive Mode Functions ──────────────────────────────────────────────

# Extract non-done epics by combining epics.md titles + sprint-status.yaml statuses
# Output: lines of "ID|Title [status]" (e.g. "13.5|API Externe... [in-progress]")
list_epics() {
    local -A epic_titles=()
    local -A epic_statuses=()

    # 1. Parse epics.md for titles (primary source)
    if [[ -f "$EPICS_FILE" ]]; then
        while IFS='|' read -r id title; do
            epic_titles["$id"]="$title"
        done < <(grep '^## Epic [0-9]' "$EPICS_FILE" | sed 's/^## Epic \([0-9.]*\): \(.*\)/\1|\2/')
    fi

    # 2. Parse sprint-status.yaml for statuses + fallback titles from comments
    if [[ -f "$SPRINT_STATUS_FILE" ]]; then
        while IFS= read -r line; do
            # Epic status: "  epic-13.5: in-progress" (skip retrospective lines)
            if [[ "$line" != *retrospective* ]] && [[ "$line" =~ ^[[:space:]]+epic-([0-9.]+):[[:space:]]+(.+)$ ]]; then
                epic_statuses["${BASH_REMATCH[1]}"]="${BASH_REMATCH[2]}"
            fi
            # Title from comment: "  # Epic 15: Canaux Telegram"
            if [[ "$line" =~ ^[[:space:]]*#[[:space:]]+Epic[[:space:]]+([0-9.]+):[[:space:]]+(.+)$ ]]; then
                [[ -z "${epic_titles[${BASH_REMATCH[1]}]+_}" ]] && epic_titles["${BASH_REMATCH[1]}"]="${BASH_REMATCH[2]}"
            fi
        done < "$SPRINT_STATUS_FILE"
    fi

    # 3. Output only non-done epics, sorted by ID
    for id in $(printf '%s\n' "${!epic_statuses[@]}" | sort -V); do
        local status="${epic_statuses[$id]}"
        [[ "$status" == "done" ]] && continue
        local title="${epic_titles[$id]:-Epic $id}"
        echo "${id}|${title} [${status}]"
    done
}

# Extract non-done stories for a given epic from epics.md + sprint-status.yaml
# Args: epic_id
# Output: lines of "ID|Title [status]" (e.g. "13.5.1|Auth API Key [in-progress]")
list_stories_for_epic() {
    local epic_id="$1"
    if [[ ! -f "$EPICS_FILE" ]]; then
        log_error "Fichier epics introuvable: $EPICS_FILE"
        return 1
    fi
    # Extract raw stories from epics.md
    local raw_stories
    raw_stories=$(awk -v eid="$epic_id" '
        BEGIN { header = "## Epic " eid ":" }
        index($0, header) == 1 { found=1; next }
        found && /^## Epic [0-9]/ { exit }
        found && /^### Story [0-9]/ { print }
    ' "$EPICS_FILE" \
        | sed 's/^### Story \([0-9.]*\): \(.*\)/\1|\2/')

    # Filter out done stories using sprint-status.yaml
    while IFS='|' read -r id title; do
        [[ -z "$id" ]] && continue
        # Convert story ID to sprint-status prefix: 16.1 -> 16-1, 13.5.1 -> 13.5-1
        local prefix="${id%.*}-${id##*.}"
        local status="backlog"
        if [[ -f "$SPRINT_STATUS_FILE" ]]; then
            local s
            s=$(grep -m1 "^  ${prefix}[^0-9]" "$SPRINT_STATUS_FILE" | sed 's/.*: *//' | tr -d '[:space:]')
            [[ -n "$s" ]] && status="$s"
        fi
        if [[ "$status" != "done" ]]; then
            echo "${id}|${title} [${status}]"
        fi
    done <<< "$raw_stories"
}

# Resolve input against items: try matching by ID first, then by line number.
# Args: choice, items_array...
# Sets: MENU_RESULT_ID, MENU_RESULT_LABEL
# Returns: 0 if resolved, 1 if not found
_resolve_choice() {
    local choice="$1"
    shift
    local -a items=("$@")
    local count=${#items[@]}

    # 1. Try exact match by ID (e.g. "14" matches item "14|...")
    for item in "${items[@]}"; do
        local id="${item%%|*}"
        if [[ "$id" == "$choice" ]]; then
            MENU_RESULT_ID="$id"
            MENU_RESULT_LABEL="${item#*|}"
            return 0
        fi
    done

    # 2. Try as line number (e.g. "16" = 16th item)
    if [[ "$choice" =~ ^[0-9]+$ ]] && (( choice >= 1 && choice <= count )); then
        local selected="${items[$((choice - 1))]}"
        MENU_RESULT_ID="${selected%%|*}"
        MENU_RESULT_LABEL="${selected#*|}"
        return 0
    fi

    return 1
}

# Generic menu with ID-first resolution and manual entry fallback
# Args: prompt_text, items_array...
# Sets: MENU_RESULT_ID, MENU_RESULT_LABEL
select_from_menu() {
    local prompt_text="$1"
    shift
    local -a items=("$@")
    local count=${#items[@]}

    if [[ $count -eq 0 ]]; then
        log_warn "Aucun element trouve. Saisie manuelle requise."
        echo ""
        while true; do
            read -rp "  Entrez l'ID: " manual_id
            [[ -z "$manual_id" ]] && { log_warn "ID vide, reessayez."; continue; }
            MENU_RESULT_ID="$manual_id"
            MENU_RESULT_LABEL="(saisie manuelle)"
            return 0
        done
    fi

    echo -e "\n  ${BOLD}${prompt_text}${NC}"
    echo -e "  ${DIM}Tapez l'ID directement (ex: 14.1) ou le numero de ligne${NC}\n"
    local i=1
    for item in "${items[@]}"; do
        local id="${item%%|*}"
        local label="${item#*|}"
        printf "  ${CYAN}%2d)${NC}  %-6s  %s\n" "$i" "$id" "$label"
        ((i++))
    done
    echo ""

    while true; do
        read -rp "  Choix (ID ou #): " choice
        [[ -z "$choice" ]] && { log_warn "Choix vide."; continue; }
        if _resolve_choice "$choice" "${items[@]}"; then
            return 0
        fi
        log_warn "Introuvable: '$choice'. Entrez un ID (ex: ${items[0]%%|*}) ou un numero (1-${count})."
    done
}

# Mode selection menu
select_mode() {
    echo -e "\n  ${BOLD}Selectionner le mode d'execution${NC}\n"
    echo -e "  ${CYAN}[1]${NC}  full     (create -> dev -> review -> fix)"
    echo -e "  ${CYAN}[2]${NC}  dev      (dev -> review -> fix)"
    echo -e "  ${CYAN}[3]${NC}  review   (review -> fix)"
    echo -e "  ${CYAN}[4]${NC}  phase    (phase unique au choix)"
    echo ""

    while true; do
        read -rp "  Choix: " choice
        case "$choice" in
            1) MODE="full"; return 0 ;;
            2) MODE="dev"; return 0 ;;
            3) MODE="review"; return 0 ;;
            4)
                # Sub-menu for single phase
                echo -e "\n  ${BOLD}Selectionner la phase${NC}\n"
                echo -e "  ${CYAN}[1]${NC}  create-story"
                echo -e "  ${CYAN}[2]${NC}  dev-story"
                echo -e "  ${CYAN}[3]${NC}  code-review"
                echo -e "  ${CYAN}[4]${NC}  fix-commit"
                echo ""
                while true; do
                    read -rp "  Choix: " phase_choice
                    case "$phase_choice" in
                        1) MODE="single"; SINGLE_PHASE="create-story"; return 0 ;;
                        2) MODE="single"; SINGLE_PHASE="dev-story"; return 0 ;;
                        3) MODE="single"; SINGLE_PHASE="code-review"; return 0 ;;
                        4) MODE="single"; SINGLE_PHASE="fix-commit"; return 0 ;;
                        *) log_warn "Choix invalide (1-4)." ;;
                    esac
                done
                ;;
            *) log_warn "Choix invalide (1-4)." ;;
        esac
    done
}

# Check if epic is required for the selected mode
epic_required() {
    if [[ "$MODE" == "full" ]]; then
        return 0
    fi
    if [[ "$MODE" == "single" && "$SINGLE_PHASE" == "create-story" ]]; then
        return 0
    fi
    return 1
}

# Select epic with optional skip
# Args: required ("true" or "false")
# Sets: MENU_RESULT_ID, MENU_RESULT_LABEL
select_epic() {
    local required="$1"
    local -a epics_list=()
    while IFS= read -r line; do
        epics_list+=("$line")
    done < <(list_epics)

    local count=${#epics_list[@]}

    if [[ "$required" == "true" ]]; then
        echo -e "\n  ${BOLD}Selectionner l'Epic${NC}"
    else
        echo -e "\n  ${BOLD}Selectionner l'Epic (optionnel — aide a filtrer les stories)${NC}"
    fi
    echo -e "  ${DIM}Tapez l'ID directement (ex: 14) ou le numero de ligne${NC}\n"

    local i=1
    for item in "${epics_list[@]}"; do
        local id="${item%%|*}"
        local label="${item#*|}"
        printf "  ${CYAN}%2d)${NC}  %-6s  %s\n" "$i" "$id" "$label"
        ((i++))
    done
    if [[ "$required" != "true" ]]; then
        echo -e "\n  ${DIM}[s] Passer (saisie story directe)${NC}"
    fi
    echo ""

    while true; do
        read -rp "  Choix (ID ou #): " choice
        # Skip option for non-required epic
        if [[ "$required" != "true" && ( "$choice" == "s" || "$choice" == "S" || -z "$choice" ) ]]; then
            MENU_RESULT_ID=""
            MENU_RESULT_LABEL=""
            return 0
        fi
        [[ -z "$choice" ]] && { log_warn "Choix vide."; continue; }
        if _resolve_choice "$choice" "${epics_list[@]}"; then
            return 0
        fi
        log_warn "Introuvable: '$choice'. Entrez un ID (ex: 14, 5.5) ou un numero (1-${count})."
    done
}

# Full interactive mode flow
interactive_mode() {
    clear
    echo ""
    echo -e "${BOLD}${CYAN}================================================================${NC}"
    echo -e "${BOLD}  BMAD Pipeline — Mode Interactif${NC}"
    echo -e "${BOLD}${CYAN}================================================================${NC}"

    # 1. Select mode
    select_mode
    clear

    # 2. Select epic
    local epic_label=""
    if epic_required; then
        select_epic "true"
        EPIC_ID="$MENU_RESULT_ID"
        epic_label="$MENU_RESULT_LABEL"
    else
        select_epic "false"
        EPIC_ID="$MENU_RESULT_ID"
        epic_label="$MENU_RESULT_LABEL"
    fi

    clear

    # 3. Select story
    local -a stories_list=()
    if [[ -n "$EPIC_ID" ]]; then
        while IFS= read -r line; do
            [[ -n "$line" ]] && stories_list+=("$line")
        done < <(list_stories_for_epic "$EPIC_ID")
    fi

    # Show stories from epic, or empty list (forces manual entry)
    select_from_menu "Selectionner la Story" "${stories_list[@]}"
    STORY_ID="$MENU_RESULT_ID"
    local story_label="$MENU_RESULT_LABEL"

    # 4. Summary and confirmation
    clear
    echo ""
    echo -e "${BOLD}${CYAN}────────────────────────────────────────${NC}"
    echo -e "  ${BOLD}Recapitulatif${NC}"
    echo -e "${BOLD}${CYAN}────────────────────────────────────────${NC}"
    if [[ "$MODE" == "single" ]]; then
        echo -e "  Mode:  ${BOLD}phase ${SINGLE_PHASE}${NC}"
    else
        echo -e "  Mode:  ${BOLD}${MODE}${NC}"
    fi
    if [[ -n "$EPIC_ID" ]]; then
        echo -e "  Epic:  ${BOLD}${EPIC_ID}${NC} — ${epic_label:-}"
    fi
    echo -e "  Story: ${BOLD}${STORY_ID}${NC} — ${story_label:-}"
    if [[ "$DRY_RUN" == "true" ]]; then
        echo -e "  ${YELLOW}(dry-run active)${NC}"
    fi
    echo -e "${BOLD}${CYAN}────────────────────────────────────────${NC}"
    echo ""
    read -rp "  [Enter] Lancer  |  [q] Quitter : " confirm
    if [[ "$confirm" == "q" || "$confirm" == "Q" ]]; then
        echo "Abandon."
        exit 0
    fi
}

# ─── Main Execution (skipped when sourced) ───────────────────────────────────
if [[ "${BMAD_SOURCED:-}" == "true" ]]; then
    return 0 2>/dev/null || exit 0
fi

# ─── Parse Arguments ─────────────────────────────────────────────────────────

INTERACTIVE=false

if [[ $# -eq 0 ]]; then
    INTERACTIVE=true
else
    # Pre-scan: collect options (--dry-run etc.) and detect -i/--interactive
    # We need two passes: first detect interactive, then parse remaining args
    declare -a REMAINING_ARGS=()
    for arg in "$@"; do
        case "$arg" in
            -i|--interactive) INTERACTIVE=true ;;
            *) REMAINING_ARGS+=("$arg") ;;
        esac
    done
    # Re-set positional parameters to remaining args
    set -- "${REMAINING_ARGS[@]}"
fi

if [[ "$INTERACTIVE" == "true" ]]; then
    # Parse any remaining CLI options (--dry-run, --verbose, --max-turns, etc.)
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --create-model)  CREATE_MODEL="$2"; shift 2 ;;
            --dev-model)     DEV_MODEL="$2"; shift 2 ;;
            --review-model)  REVIEW_MODEL="$2"; shift 2 ;;
            --fix-model)     FIX_MODEL="$2"; shift 2 ;;
            --max-turns)     MAX_TURNS="$2"; shift 2 ;;
            --dry-run)       DRY_RUN=true; shift ;;
            --no-commit)     NO_COMMIT=true; shift ;;
            --verbose)       VERBOSE=true; shift ;;
            -h|--help)       usage ;;
            *)               die "Argument inconnu en mode interactif: $1 (seules les options sont acceptees)" ;;
        esac
    done
    interactive_mode
else
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --story)         STORY_ID="$2"; shift 2 ;;
            --epic)          EPIC_ID="$2"; shift 2 ;;
            --full)          MODE="full"; shift ;;
            --dev)           MODE="dev"; shift ;;
            --review)        MODE="review"; shift ;;
            --phase)         MODE="single"; SINGLE_PHASE="$2"; shift 2 ;;
            --create-model)  CREATE_MODEL="$2"; shift 2 ;;
            --dev-model)     DEV_MODEL="$2"; shift 2 ;;
            --review-model)  REVIEW_MODEL="$2"; shift 2 ;;
            --fix-model)     FIX_MODEL="$2"; shift 2 ;;
            --max-turns)     MAX_TURNS="$2"; shift 2 ;;
            --dry-run)       DRY_RUN=true; shift ;;
            --no-commit)     NO_COMMIT=true; shift ;;
            --verbose)       VERBOSE=true; shift ;;
            -h|--help)       usage ;;
            *)               die "Argument inconnu: $1 (voir --help)" ;;
        esac
    done
fi

# ─── Validate ────────────────────────────────────────────────────────────────

[[ -z "$STORY_ID" ]] && die "--story est obligatoire"
[[ -z "$MODE" ]] && die "Un mode est requis: --full, --dev, --review, ou --phase <name>"

if [[ "$MODE" == "full" && -z "$EPIC_ID" ]]; then
    die "--epic est obligatoire avec --full"
fi

if [[ "$MODE" == "single" && "$SINGLE_PHASE" == "create-story" && -z "$EPIC_ID" ]]; then
    die "--epic est obligatoire pour la phase create-story"
fi

if [[ "$MODE" == "single" ]]; then
    case "$SINGLE_PHASE" in
        create-story|dev-story|code-review|fix-commit) ;;
        *) die "Phase invalide: $SINGLE_PHASE (valides: create-story, dev-story, code-review, fix-commit)" ;;
    esac
fi

command -v claude >/dev/null 2>&1 || die "claude CLI non trouve dans PATH. Installez Claude Code."

# ─── Determine Phases ────────────────────────────────────────────────────────

declare -a PHASES=()

case "$MODE" in
    full)    PHASES=(create-story dev-story code-review fix-commit) ;;
    dev)     PHASES=(dev-story code-review fix-commit) ;;
    review)  PHASES=(code-review fix-commit) ;;
    single)  PHASES=("$SINGLE_PHASE") ;;
esac

# ─── Setup ───────────────────────────────────────────────────────────────────

mkdir -p "$LOGS_DIR"

# Save pipeline config to log
cat > "$LOGS_DIR/pipeline-config.json" <<EOF
{
  "story_id": "${STORY_ID}",
  "epic_id": "${EPIC_ID}",
  "mode": "${MODE}",
  "phases": [$(printf '"%s",' "${PHASES[@]}" | sed 's/,$//')],
  "models": {
    "create-story": "${CREATE_MODEL}",
    "dev-story": "${DEV_MODEL}",
    "code-review": "${REVIEW_MODEL}",
    "fix-commit": "${FIX_MODEL}"
  },
  "max_turns": ${MAX_TURNS},
  "dry_run": ${DRY_RUN},
  "no_commit": ${NO_COMMIT},
  "timestamp": "${TIMESTAMP}"
}
EOF

# ─── Execute Pipeline ────────────────────────────────────────────────────────

echo ""
log_info "BMAD Pipeline — Story ${STORY_ID} — Mode: ${MODE}"
log_info "Phases: ${PHASES[*]}"
log_info "Logs: ${LOGS_DIR}"

BASELINE_SHA=$(git rev-parse HEAD 2>/dev/null || echo "none")
CHANGED_FILES=""

for phase in "${PHASES[@]}"; do
    prompt=""
    case "$phase" in
        create-story)
            prompt=$(prompt_create_story)
            run_phase "$phase" "$CREATE_MODEL" "$TOOLS_CREATE" "$prompt"
            ;;
        dev-story)
            prompt=$(prompt_dev_story)
            run_phase "$phase" "$DEV_MODEL" "$TOOLS_DEV" "$prompt"
            # Capture changed files after dev for subsequent phases
            CHANGED_FILES=$(get_changed_files "$BASELINE_SHA")
            ;;
        code-review)
            [[ -z "$CHANGED_FILES" ]] && CHANGED_FILES=$(get_changed_files "$BASELINE_SHA")
            prompt=$(prompt_code_review "$CHANGED_FILES")
            run_phase "$phase" "$REVIEW_MODEL" "$TOOLS_REVIEW" "$prompt"
            ;;
        fix-commit)
            [[ -z "$CHANGED_FILES" ]] && CHANGED_FILES=$(get_changed_files "$BASELINE_SHA")
            review_log_path="$LOGS_DIR/code-review.log"
            prompt=$(prompt_fix_commit "$CHANGED_FILES" "$review_log_path")
            run_phase "$phase" "$FIX_MODEL" "$TOOLS_FIX" "$prompt"
            ;;
    esac
done

# ─── Final Report ────────────────────────────────────────────────────────────

print_summary
