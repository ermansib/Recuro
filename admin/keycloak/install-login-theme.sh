#!/usr/bin/env bash
# Puts the Recuro sign-in pages (admin/keycloak-themes/recuro) on a Keycloak that is already running in
# Docker, and points the recuro-portal client at them (RCU-PLT-001). Safe to run again after every pull.
#
# The realm is imported with IGNORE_EXISTING, so a Keycloak created before the theme existed never gets it
# or the client's login_theme from recuro-realm.json. This script adds both without recreating the
# container (start-dev keeps its data inside it) and without touching anything else in the realm.
#
#   admin/keycloak/install-login-theme.sh [container]
#
# KEYCLOAK_ADMIN_USER / KEYCLOAK_ADMIN_PASSWORD: the bootstrap admin (default admin / admin, as in compose).
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
theme="$here/../keycloak-themes/recuro"
user="${KEYCLOAK_ADMIN_USER:-admin}"
pass="${KEYCLOAK_ADMIN_PASSWORD:-admin}"

container="${1:-}"
if [ -z "$container" ]; then
  container="$(docker ps --format '{{.Names}} {{.Image}}' | awk 'tolower($2) ~ /keycloak/ { print $1; exit }')"
fi
if [ -z "$container" ]; then
  echo "No running Keycloak container found. Pass its name: $0 <container>" >&2
  exit 1
fi
echo "Keycloak container: $container"

# Compose files from PR #61 on mount the themes folder read-only; then the files are already current.
if docker inspect -f '{{range .Mounts}}{{println .Destination}}{{end}}' "$container" | grep -qx '/opt/keycloak/themes'; then
  echo "Themes folder is mounted from the repo: nothing to copy."
else
  docker exec "$container" mkdir -p /opt/keycloak/themes/recuro
  docker cp "$theme/." "$container:/opt/keycloak/themes/recuro"
  echo "Copied the Recuro theme into the container."
fi

kcadm() { docker exec "$container" /opt/keycloak/bin/kcadm.sh "$@" --config /tmp/recuro-kcadm.config; }
kcadm config credentials --server http://localhost:8080 --realm master --user "$user" --password "$pass" >/dev/null
id="$(kcadm get clients -r recuro -q clientId=recuro-portal --fields id --format csv --noquotes | tr -d '\r')"
if [ -z "$id" ]; then
  echo "The recuro realm has no recuro-portal client." >&2
  exit 1
fi
# Only the login_theme attribute changes; kcadm keeps the rest of the client as it is.
kcadm update "clients/$id" -r recuro -s 'attributes.login_theme=recuro'
docker exec "$container" rm -f /tmp/recuro-kcadm.config
echo "recuro-portal now uses the Recuro login theme. Sign in from the portal to see it."
