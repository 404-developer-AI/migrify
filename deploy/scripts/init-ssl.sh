#!/bin/bash
# Initialize SSL certificates for Migrify
# Called by the install script — not meant to be run standalone.
#
# Flow:
# 1. Temporarily use HTTP-only Nginx config
# 2. Start Nginx so Certbot can do the ACME challenge
# 3. Request certificate via Certbot
# 4. Switch to full SSL Nginx config and restart

set -e

DOMAIN="$1"
EMAIL="$2"
INSTALL_DIR="$3"

if [ -z "$DOMAIN" ] || [ -z "$EMAIL" ] || [ -z "$INSTALL_DIR" ]; then
    echo "Usage: init-ssl.sh <domain> <email> <install-dir>"
    exit 1
fi

NGINX_TEMPLATES="$INSTALL_DIR/deploy/nginx"

# Step 1: Use HTTP-only config for ACME challenge
echo "Starting Nginx with HTTP-only config for ACME challenge..."
cp "$NGINX_TEMPLATES/default.conf.template" "$NGINX_TEMPLATES/default.conf.template.bak"
cp "$NGINX_TEMPLATES/http-only.conf.template" "$NGINX_TEMPLATES/default.conf.template"

# Step 2: Start/restart Nginx
docker compose -f "$INSTALL_DIR/docker-compose.yml" up -d nginx
sleep 3

# Step 3: Request certificate
echo "Requesting SSL certificate for $DOMAIN..."
docker compose -f "$INSTALL_DIR/docker-compose.yml" run --rm certbot certonly \
    --webroot \
    --webroot-path=/var/www/certbot \
    --email "$EMAIL" \
    --agree-tos \
    --no-eff-email \
    --domain "$DOMAIN" \
    --non-interactive

# Step 4: Restore full SSL config
echo "Switching to SSL-enabled Nginx config..."
mv "$NGINX_TEMPLATES/default.conf.template.bak" "$NGINX_TEMPLATES/default.conf.template"

# Step 5: Restart Nginx with SSL
docker compose -f "$INSTALL_DIR/docker-compose.yml" restart nginx

echo "SSL certificate for $DOMAIN obtained and Nginx restarted with HTTPS."
