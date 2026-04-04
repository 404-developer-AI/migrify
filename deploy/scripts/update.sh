#!/bin/bash
# ============================================================
# Migrify — One-string updater
# Usage: curl -fsSL https://raw.githubusercontent.com/404-developer-AI/migrify/main/deploy/scripts/update.sh | bash
# ============================================================

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m'
BOLD='\033[1m'

INSTALL_DIR="/opt/migrify"

print_header() {
    echo ""
    echo -e "${CYAN}╔══════════════════════════════════════════════════╗${NC}"
    echo -e "${CYAN}║${NC}  ${BOLD}Migrify Updater${NC}                                 ${CYAN}║${NC}"
    echo -e "${CYAN}╚══════════════════════════════════════════════════╝${NC}"
    echo ""
}

print_step() {
    echo -e "${BLUE}[$(date '+%H:%M:%S')]${NC} ${BOLD}$1${NC}"
}

print_ok() {
    echo -e "${GREEN}  ✓ $1${NC}"
}

print_warn() {
    echo -e "${YELLOW}  ⚠ $1${NC}"
}

print_error() {
    echo -e "${RED}  ✗ $1${NC}"
}

# ---- Pre-flight checks ----

check_root() {
    if [ "$EUID" -ne 0 ]; then
        print_error "This script must be run as root (use sudo)."
        exit 1
    fi
}

check_installation() {
    if [ ! -d "$INSTALL_DIR" ] || [ ! -f "$INSTALL_DIR/.env" ]; then
        print_error "No Migrify installation found at $INSTALL_DIR"
        print_error "Run the install script first."
        exit 1
    fi

    if [ ! -f "$INSTALL_DIR/docker-compose.yml" ]; then
        print_error "docker-compose.yml not found at $INSTALL_DIR"
        exit 1
    fi
}

# ---- Update steps ----

get_current_version() {
    cd "$INSTALL_DIR"
    # Try to get current app version from health endpoint
    CURRENT_VERSION=$(docker compose exec app curl -sf http://localhost:8080/health 2>/dev/null | grep -o '"status":"healthy"' > /dev/null && echo "running" || echo "unknown")
    OLD_COMMIT=$(git rev-parse --short HEAD 2>/dev/null || echo "unknown")
}

pull_latest_code() {
    print_step "Pulling latest code..."

    cd "$INSTALL_DIR"
    git fetch --quiet origin
    git reset --hard origin/main --quiet
    NEW_COMMIT=$(git rev-parse --short HEAD)

    if [ "$OLD_COMMIT" = "$NEW_COMMIT" ]; then
        print_ok "Already up to date ($NEW_COMMIT)"
    else
        print_ok "Updated: $OLD_COMMIT → $NEW_COMMIT"
    fi
}

rebuild_and_restart() {
    print_step "Pulling latest app image..."
    cd "$INSTALL_DIR"
    if docker compose pull app 2>/dev/null; then
        print_ok "Image pulled from registry"
    else
        print_warn "Could not pull from registry — building locally..."
        docker compose build --quiet app
        print_ok "Image built locally"
    fi

    print_step "Restarting containers..."
    docker compose up -d --remove-orphans
    print_ok "Containers restarted"
}

wait_for_healthy() {
    print_step "Waiting for app to become healthy..."
    cd "$INSTALL_DIR"

    for i in $(seq 1 30); do
        if docker compose exec app curl -sf http://localhost:8080/health > /dev/null 2>&1; then
            print_ok "App is healthy"
            return
        fi
        echo -n "."
        sleep 3
    done

    echo ""
    print_warn "App did not become healthy within 90 seconds"
    print_warn "Check logs: docker compose -f $INSTALL_DIR/docker-compose.yml logs app"
}

print_summary() {
    cd "$INSTALL_DIR"
    echo ""
    echo -e "${GREEN}╔══════════════════════════════════════════════════╗${NC}"
    echo -e "${GREEN}║${NC}  ${BOLD}Update complete!${NC}                                ${GREEN}║${NC}"
    echo -e "${GREEN}╚══════════════════════════════════════════════════╝${NC}"
    echo ""
    echo -e "  ${BOLD}Commit:${NC}  $OLD_COMMIT → $NEW_COMMIT"
    echo ""
    echo -e "  ${BOLD}Container status:${NC}"
    docker compose ps --format "  {{.Name}}  {{.Status}}" 2>/dev/null || true
    echo ""
    echo -e "  ${BOLD}Note:${NC} Database migrations are applied automatically on startup."
    echo -e "  ${BOLD}Note:${NC} All data (database, logs, certificates) is preserved."
    echo ""
}

# ---- Main ----

main() {
    print_header
    check_root
    check_installation
    get_current_version
    pull_latest_code
    rebuild_and_restart
    wait_for_healthy
    print_summary
}

main "$@"
