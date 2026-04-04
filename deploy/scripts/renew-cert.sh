#!/bin/bash
# Renew SSL certificates for Migrify
# Can be added to crontab: 0 3 * * * /opt/migrify/deploy/scripts/renew-cert.sh

set -e

INSTALL_DIR="${1:-/opt/migrify}"

docker compose -f "$INSTALL_DIR/docker-compose.yml" run --rm certbot renew --webroot -w /var/www/certbot --quiet
docker compose -f "$INSTALL_DIR/docker-compose.yml" exec nginx nginx -s reload
