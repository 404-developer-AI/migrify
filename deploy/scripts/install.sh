#!/bin/bash
# ============================================================
# Migrify — One-string installer
# Usage: curl -fsSL https://raw.githubusercontent.com/404-developer-AI/migrify/main/deploy/scripts/install.sh | bash
# ============================================================

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color
BOLD='\033[1m'

INSTALL_DIR="/opt/migrify"
REPORT_FILE="$INSTALL_DIR/install-report.log"
REPO_URL="https://github.com/404-developer-AI/migrify.git"
ENABLE_SSL="false"

# ---- Helper functions ----

print_header() {
    echo ""
    echo -e "${CYAN}╔══════════════════════════════════════════════════╗${NC}"
    echo -e "${CYAN}║${NC}  ${BOLD}Migrify Installer${NC}                               ${CYAN}║${NC}"
    echo -e "${CYAN}║${NC}  Email migration: IMAP → Microsoft 365            ${CYAN}║${NC}"
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

generate_password() {
    openssl rand -base64 18 | tr -d '/+=' | head -c 24
}

generate_encryption_key() {
    openssl rand -base64 32
}

ask() {
    local prompt="$1"
    local default="$2"
    local var_name="$3"
    local is_secret="${4:-false}"

    if [ -n "$default" ]; then
        prompt="$prompt [${default}]"
    fi

    echo -en "${CYAN}  ? ${NC}${prompt}: "
    if [ "$is_secret" = "true" ]; then
        read -s input
        echo ""
    else
        read input
    fi

    input="${input:-$default}"
    eval "$var_name=\"$input\""
}

ask_yes_no() {
    local prompt="$1"
    local default="${2:-y}"
    local var_name="$3"

    if [ "$default" = "y" ]; then
        prompt="$prompt [Y/n]"
    else
        prompt="$prompt [y/N]"
    fi

    echo -en "${CYAN}  ? ${NC}${prompt}: "
    read input
    input="${input:-$default}"

    case "$input" in
        [yY]|[yY][eE][sS]) eval "$var_name=true" ;;
        *) eval "$var_name=false" ;;
    esac
}

# ---- Pre-flight checks ----

check_root() {
    if [ "$EUID" -ne 0 ]; then
        print_error "This script must be run as root (use sudo)."
        exit 1
    fi
}

check_os() {
    if [ ! -f /etc/os-release ]; then
        print_error "Cannot detect OS. This installer supports Ubuntu/Debian."
        exit 1
    fi
    . /etc/os-release
    if [[ "$ID" != "ubuntu" && "$ID" != "debian" ]]; then
        print_warn "This installer is tested on Ubuntu/Debian. Your OS ($ID) may work but is not officially supported."
    fi
}

# ---- Installation steps ----

install_dependencies() {
    print_step "Installing system dependencies..."

    apt-get update -qq
    apt-get install -y -qq git curl openssl ca-certificates gnupg lsb-release > /dev/null 2>&1
    print_ok "System dependencies installed"
}

install_docker() {
    if command -v docker &> /dev/null; then
        print_ok "Docker is already installed ($(docker --version | cut -d' ' -f3 | tr -d ','))"
        return
    fi

    print_step "Installing Docker..."

    # Add Docker GPG key
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc

    # Add Docker repo
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu \
        $(. /etc/os-release && echo "$VERSION_CODENAME") stable" > /etc/apt/sources.list.d/docker.list

    apt-get update -qq
    apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin > /dev/null 2>&1

    systemctl enable docker
    systemctl start docker

    print_ok "Docker installed ($(docker --version | cut -d' ' -f3 | tr -d ','))"
}

check_existing_installation() {
    if [ -d "$INSTALL_DIR" ] && [ -f "$INSTALL_DIR/.env" ]; then
        print_warn "Existing Migrify installation detected at $INSTALL_DIR"
        ask_yes_no "Do you want to reinstall? This will overwrite the configuration but keep your data" "n" REINSTALL
        if [ "$REINSTALL" != "true" ]; then
            echo ""
            print_warn "Installation cancelled. Use the update script to update an existing installation."
            exit 0
        fi
        # Stop existing containers
        print_step "Stopping existing containers..."
        cd "$INSTALL_DIR"
        docker compose down 2>/dev/null || true
    fi
}

collect_configuration() {
    print_step "Configuration"
    echo ""
    echo -e "  Please answer the following questions to configure Migrify."
    echo -e "  Press Enter to accept the default value shown in brackets."
    echo ""

    # Domain
    ask "Domain name or IP address for Migrify" "" MIGRIFY_DOMAIN
    while [ -z "$MIGRIFY_DOMAIN" ]; do
        print_error "Domain name or IP is required (e.g., migrify.example.com or 192.168.1.100)"
        ask "Domain name or IP address for Migrify" "" MIGRIFY_DOMAIN
    done

    # SSL
    ask_yes_no "Enable SSL with Let's Encrypt? (requires public domain with DNS pointing to this server)" "y" ENABLE_SSL
    if [ "$ENABLE_SSL" = "true" ]; then
        ask "Email for Let's Encrypt SSL certificate" "" LETSENCRYPT_EMAIL
        while [ -z "$LETSENCRYPT_EMAIL" ]; do
            print_error "Email is required for Let's Encrypt"
            ask "Email for Let's Encrypt SSL certificate" "" LETSENCRYPT_EMAIL
        done
    else
        LETSENCRYPT_EMAIL=""
        print_warn "SSL disabled — app will be accessible via HTTP only"
    fi

    # PostgreSQL password
    local default_pg_pass
    default_pg_pass=$(generate_password)
    ask_yes_no "Generate a random PostgreSQL password?" "y" AUTO_PG_PASS
    if [ "$AUTO_PG_PASS" = "true" ]; then
        POSTGRES_PASSWORD="$default_pg_pass"
        print_ok "PostgreSQL password generated"
    else
        ask "Enter PostgreSQL password (min 12 characters)" "" POSTGRES_PASSWORD "true"
        while [ ${#POSTGRES_PASSWORD} -lt 12 ]; do
            print_error "Password must be at least 12 characters"
            ask "Enter PostgreSQL password" "" POSTGRES_PASSWORD "true"
        done
    fi

    # PostgreSQL user
    ask "PostgreSQL username" "migrify_user" POSTGRES_USER

    # Admin email
    ask "Admin email for web login" "admin@migrify.local" ADMIN_EMAIL

    # Admin password
    local default_admin_pass
    default_admin_pass=$(generate_password)
    ask_yes_no "Generate a random admin password?" "y" AUTO_ADMIN_PASS
    if [ "$AUTO_ADMIN_PASS" = "true" ]; then
        ADMIN_PASSWORD="$default_admin_pass"
        print_ok "Admin password generated"
    else
        ask "Enter admin password (min 8 chars, uppercase + lowercase + digit)" "" ADMIN_PASSWORD "true"
        while [ ${#ADMIN_PASSWORD} -lt 8 ]; do
            print_error "Password must be at least 8 characters"
            ask "Enter admin password" "" ADMIN_PASSWORD "true"
        done
    fi

    # Encryption key (always auto-generated)
    MIGRIFY_ENCRYPTION_KEY=$(generate_encryption_key)
    print_ok "Encryption key generated"

    echo ""
}

confirm_configuration() {
    print_step "Configuration summary"
    echo ""
    echo -e "  ${BOLD}Domain:${NC}           $MIGRIFY_DOMAIN"
    if [ "$ENABLE_SSL" = "true" ]; then
        echo -e "  ${BOLD}SSL:${NC}              Enabled (Let's Encrypt)"
        echo -e "  ${BOLD}SSL Email:${NC}        $LETSENCRYPT_EMAIL"
    else
        echo -e "  ${BOLD}SSL:${NC}              Disabled (HTTP only)"
    fi
    echo -e "  ${BOLD}PostgreSQL User:${NC}  $POSTGRES_USER"
    echo -e "  ${BOLD}PostgreSQL Pass:${NC}  ********"
    echo -e "  ${BOLD}Admin Email:${NC}      $ADMIN_EMAIL"
    echo -e "  ${BOLD}Admin Pass:${NC}       ********"
    echo -e "  ${BOLD}Install Dir:${NC}      $INSTALL_DIR"
    echo ""

    ask_yes_no "Proceed with installation?" "y" PROCEED
    if [ "$PROCEED" != "true" ]; then
        echo ""
        print_warn "Installation cancelled."
        exit 0
    fi
}

clone_repository() {
    print_step "Downloading Migrify..."

    if [ -d "$INSTALL_DIR/.git" ]; then
        cd "$INSTALL_DIR"
        git pull --quiet
        print_ok "Repository updated"
    else
        mkdir -p "$INSTALL_DIR"
        git clone --quiet "$REPO_URL" "$INSTALL_DIR"
        print_ok "Repository cloned to $INSTALL_DIR"
    fi
}

create_env_file() {
    print_step "Creating configuration file..."

    cat > "$INSTALL_DIR/.env" <<EOF
# Migrify Environment Configuration
# Generated by installer on $(date -u '+%Y-%m-%d %H:%M:%S UTC')
# WARNING: Contains secrets — do not commit to git.

# Domain
MIGRIFY_DOMAIN=$MIGRIFY_DOMAIN

# SSL
ENABLE_SSL=$ENABLE_SSL
LETSENCRYPT_EMAIL=$LETSENCRYPT_EMAIL

# PostgreSQL
POSTGRES_USER=$POSTGRES_USER
POSTGRES_PASSWORD=$POSTGRES_PASSWORD

# Encryption key (AES-256-GCM, base64)
MIGRIFY_ENCRYPTION_KEY=$MIGRIFY_ENCRYPTION_KEY

# Admin login
ADMIN_EMAIL=$ADMIN_EMAIL
ADMIN_PASSWORD=$ADMIN_PASSWORD
EOF

    chmod 600 "$INSTALL_DIR/.env"
    print_ok "Configuration written to $INSTALL_DIR/.env"
}

setup_nginx_config() {
    print_step "Configuring Nginx..."

    local nginx_dir="$INSTALL_DIR/deploy/nginx"

    if [ "$ENABLE_SSL" = "true" ]; then
        cp "$nginx_dir/default.conf.template" "$nginx_dir/active.conf.template"
        print_ok "Nginx configured with SSL"
    else
        cp "$nginx_dir/no-ssl.conf.template" "$nginx_dir/active.conf.template"
        print_ok "Nginx configured without SSL (HTTP only)"
    fi
}

build_and_start() {
    print_step "Building and starting Migrify..."

    cd "$INSTALL_DIR"

    # Pull pre-built image from ghcr.io, fall back to local build
    print_ok "Pulling Docker image..."
    if docker compose pull app 2>/dev/null; then
        print_ok "Image pulled from registry"
    else
        print_warn "Could not pull from registry — building locally (this may take a few minutes)..."
        docker compose build --quiet app
        print_ok "Image built locally"
    fi

    # Start database first, wait for healthy
    print_ok "Starting PostgreSQL..."
    docker compose up -d db
    echo -n "  Waiting for database"
    for i in $(seq 1 30); do
        if docker compose exec db pg_isready -U "$POSTGRES_USER" -d migrify -q 2>/dev/null; then
            echo ""
            print_ok "PostgreSQL is ready"
            break
        fi
        echo -n "."
        sleep 2
        if [ $i -eq 30 ]; then
            echo ""
            print_error "PostgreSQL failed to start within 60 seconds"
            docker compose logs db
            exit 1
        fi
    done

    # Start app, wait for healthy
    print_ok "Starting Migrify app..."
    docker compose up -d app
    echo -n "  Waiting for app"
    for i in $(seq 1 30); do
        if docker compose exec app curl -sf http://localhost:8080/health > /dev/null 2>&1; then
            echo ""
            print_ok "Migrify app is ready"
            break
        fi
        echo -n "."
        sleep 3
        if [ $i -eq 30 ]; then
            echo ""
            print_error "App failed to start within 90 seconds"
            docker compose logs app
            exit 1
        fi
    done
}

setup_ssl() {
    if [ "$ENABLE_SSL" != "true" ]; then
        return
    fi

    print_step "Setting up SSL certificate..."

    cd "$INSTALL_DIR"
    bash "$INSTALL_DIR/deploy/scripts/init-ssl.sh" "$MIGRIFY_DOMAIN" "$LETSENCRYPT_EMAIL" "$INSTALL_DIR"
    print_ok "SSL certificate active"
}

start_nginx() {
    print_step "Starting Nginx..."

    cd "$INSTALL_DIR"
    docker compose up -d nginx
    sleep 3

    if [ "$ENABLE_SSL" = "true" ]; then
        # Verify HTTPS
        if curl -sf --max-time 5 "https://$MIGRIFY_DOMAIN/health" > /dev/null 2>&1; then
            print_ok "HTTPS is working"
        else
            print_warn "HTTPS check failed — the app may still be starting, or DNS is not yet pointing to this server"
        fi
    else
        # Verify HTTP
        if curl -sf --max-time 5 "http://$MIGRIFY_DOMAIN/health" > /dev/null 2>&1; then
            print_ok "HTTP is working"
        else
            # Also try localhost in case domain doesn't resolve yet
            if curl -sf --max-time 5 "http://localhost/health" > /dev/null 2>&1; then
                print_ok "HTTP is working (via localhost)"
            else
                print_warn "HTTP check failed — the app may still be starting"
            fi
        fi
    fi
}

setup_cert_renewal() {
    if [ "$ENABLE_SSL" != "true" ]; then
        return
    fi

    print_step "Setting up automatic SSL renewal..."

    cd "$INSTALL_DIR"

    # Start certbot renewal container
    docker compose --profile ssl up -d certbot

    # Also add crontab as backup
    CRON_CMD="0 3 * * * $INSTALL_DIR/deploy/scripts/renew-cert.sh $INSTALL_DIR >> /var/log/migrify-certbot.log 2>&1"
    (crontab -l 2>/dev/null | grep -v "migrify.*renew-cert" ; echo "$CRON_CMD") | crontab -

    chmod +x "$INSTALL_DIR/deploy/scripts/renew-cert.sh"
    print_ok "Auto-renewal configured (daily check at 03:00)"
}

write_report() {
    print_step "Writing installation report..."

    if [ "$ENABLE_SSL" = "true" ]; then
        ACCESS_URL="https://$MIGRIFY_DOMAIN"
        SSL_SECTION="SSL CERTIFICATE
  Provider:       Let's Encrypt
  Email:          $LETSENCRYPT_EMAIL
  Domain:         $MIGRIFY_DOMAIN
  Auto-renewal:   Enabled (daily check at 03:00)"
    else
        ACCESS_URL="http://$MIGRIFY_DOMAIN"
        SSL_SECTION="SSL CERTIFICATE
  Status:         Disabled (HTTP only)
  Note:           Run the installer again with SSL enabled when ready"
    fi

    cat > "$REPORT_FILE" <<EOF
================================================================================
  MIGRIFY — INSTALLATION REPORT
  Generated: $(date -u '+%Y-%m-%d %H:%M:%S UTC')
================================================================================

STATUS: Installation completed successfully

INSTALLATION DIRECTORY
  $INSTALL_DIR

ACCESS
  URL:            $ACCESS_URL
  Admin Email:    $ADMIN_EMAIL
  Admin Password: $ADMIN_PASSWORD

POSTGRESQL DATABASE
  Host:           db (internal Docker network)
  Port:           5432
  Database:       migrify
  Username:       $POSTGRES_USER
  Password:       $POSTGRES_PASSWORD

ENCRYPTION
  Key (base64):   $MIGRIFY_ENCRYPTION_KEY

$SSL_SECTION

DOCKER CONTAINERS
$(docker compose -f "$INSTALL_DIR/docker-compose.yml" ps --format "  {{.Name}}\t{{.Status}}" 2>/dev/null || echo "  (could not retrieve container status)")

DOCKER VOLUMES
  migrify-pgdata          PostgreSQL data
  migrify-logs            Application logs
  migrify-certbot-webroot Certbot webroot
  migrify-certbot-certs   SSL certificates

CONFIGURATION FILE
  $INSTALL_DIR/.env

UPDATE COMMAND
  curl -fsSL https://raw.githubusercontent.com/404-developer-AI/migrify/main/deploy/scripts/update.sh | bash

================================================================================
  WARNING: This file contains passwords and encryption keys.
  Read and save what you need, then DELETE this file:
  rm $REPORT_FILE
================================================================================
EOF

    chmod 600 "$REPORT_FILE"
    print_ok "Report written to $REPORT_FILE"
}

print_summary() {
    if [ "$ENABLE_SSL" = "true" ]; then
        ACCESS_URL="https://$MIGRIFY_DOMAIN"
    else
        ACCESS_URL="http://$MIGRIFY_DOMAIN"
    fi

    echo ""
    echo -e "${GREEN}╔══════════════════════════════════════════════════╗${NC}"
    echo -e "${GREEN}║${NC}  ${BOLD}Installation complete!${NC}                          ${GREEN}║${NC}"
    echo -e "${GREEN}╚══════════════════════════════════════════════════╝${NC}"
    echo ""
    echo -e "  ${BOLD}URL:${NC}            $ACCESS_URL"
    echo -e "  ${BOLD}Admin login:${NC}    $ADMIN_EMAIL"
    echo -e "  ${BOLD}Admin password:${NC} $ADMIN_PASSWORD"
    echo ""
    if [ "$ENABLE_SSL" != "true" ]; then
        echo -e "  ${YELLOW}Note: SSL is disabled. To enable later, re-run the installer.${NC}"
        echo ""
    fi
    echo -e "  ${BOLD}Install report:${NC} $REPORT_FILE"
    echo ""
    echo -e "${YELLOW}  ╔═════════════════════════════════════════════════════════���══╗${NC}"
    echo -e "${YELLOW}  ║${NC}  ${BOLD}${RED}WARNING:${NC} The install report contains passwords.           ${YELLOW}║${NC}"
    echo -e "${YELLOW}  ║${NC}  Save what you need, then delete the report:             ${YELLOW}║${NC}"
    echo -e "${YELLOW}  ║${NC}  ${BOLD}rm $REPORT_FILE${NC}$(printf '%*s' $((23 - ${#REPORT_FILE})) '')${YELLOW}║${NC}"
    echo -e "${YELLOW}  ╚════════════════════════════════════════════════════════════╝${NC}"
    echo ""
    echo -e "  To update later:  ${CYAN}curl -fsSL https://raw.githubusercontent.com/404-developer-AI/migrify/main/deploy/scripts/update.sh | bash${NC}"
    echo ""
}

# ---- Main ----

main() {
    print_header
    check_root
    check_os
    install_dependencies
    install_docker
    check_existing_installation

    echo ""
    collect_configuration
    confirm_configuration

    clone_repository
    create_env_file
    setup_nginx_config
    build_and_start
    setup_ssl
    start_nginx
    setup_cert_renewal
    write_report
    print_summary
}

main "$@"
