#!/usr/bin/env bash
# Write Production config files for API + Web (step 5).
# Run on the server after dirs exist; does not start services.
#
# Usage:
#   sudo SCHNAPP_DB_PASSWORD='…' SCHNAPP_ADMIN_PASSWORD='…' \
#     ./scripts/deployment/host/write-server-config.sh
#
#   ./scripts/deployment/host/write-server-config.sh \
#     --prefix /opt/schnapp \
#     --db-password '…' \
#     --admin-email you@example.com \
#     --admin-password '…' \
#     --api-bind 127.0.0.1,0.0.0.0 --api-port 5080 \
#     --web-bind 0.0.0.0 --web-port 8080 \
#     --api-url http://127.0.0.1:5080
#
# Listen addresses (ASPNETCORE_URLS):
#   --api-bind / --web-bind   comma-separated hosts/IPs (default: 127.0.0.1)
#   --api-port / --web-port   TCP ports (default: 5080 / 5081)
#   --api-urls / --web-urls   full override, e.g. "http://127.0.0.1:5080;http://0.0.0.0:5080"
#                             (wins over bind+port)
#
# Writes:
#   {prefix}/config/api.appsettings.Production.json
#   {prefix}/config/web.appsettings.Production.json
#   {prefix}/config/api.env          (systemd EnvironmentFile)
#   {prefix}/config/web.env
# And optionally symlinks into published app dirs if --link-into-apps is set.
set -euo pipefail

PREFIX="${SCHNAPP_PREFIX:-/opt/schnapp}"
DB_PROVIDER="${SCHNAPP_DB_PROVIDER:-postgresql}"
DB_HOST="${SCHNAPP_DB_HOST:-127.0.0.1}"
DB_PORT="${SCHNAPP_DB_PORT:-}"
DB_NAME="${SCHNAPP_DB_NAME:-schnapp}"
DB_USER="${SCHNAPP_DB_USER:-schnapp}"
DB_PASSWORD="${SCHNAPP_DB_PASSWORD:-}"
FIREBIRD_DIR="${SCHNAPP_FIREBIRD_DIR:-}"
BLOB_ROOT=""

API_BIND="${SCHNAPP_API_BIND:-127.0.0.1}"
WEB_BIND="${SCHNAPP_WEB_BIND:-127.0.0.1}"
API_PORT="${SCHNAPP_API_PORT:-5080}"
WEB_PORT="${SCHNAPP_WEB_PORT:-5081}"
API_URLS="${SCHNAPP_API_URLS:-}"
WEB_URLS="${SCHNAPP_WEB_URLS:-}"
# Web BFF → API base (outbound). Empty = derive from first API listen URL.
API_URL="${SCHNAPP_API_URL:-}"

ADMIN_EMAIL="${SCHNAPP_ADMIN_EMAIL:-}"
ADMIN_PASSWORD="${SCHNAPP_ADMIN_PASSWORD:-}"
LINK_INTO_APPS=0
FORCE=0

usage() {
  sed -n '2,28p' "$0" | sed 's/^# \{0,1\}//'
  cat <<'EOF'

Options:
  -p, --prefix DIR
  --provider NAME         postgresql (default) or firebird
  --db-host HOST          default 127.0.0.1
  --db-port PORT          default 5432 (postgresql) or 3050 (firebird)
  --db-name NAME          default schnapp (Postgres DB name or Firebird file stem)
  --db-user USER          default schnapp
  --db-password PASS      or SCHNAPP_DB_PASSWORD
  --firebird-dir DIR      Firebird .fdb directory (default {prefix}/data/firebird)
  --blob-root DIR         default {prefix}/data/blobs

  Listen (Kestrel ASPNETCORE_URLS):
  --api-bind HOSTS        comma-separated (default 127.0.0.1); e.g. 127.0.0.1,0.0.0.0
  --web-bind HOSTS        comma-separated (default 127.0.0.1)
  --api-port PORT         default 5080
  --web-port PORT         default 5081
  --api-urls URLS         full ASPNETCORE_URLS for API (overrides bind/port)
  --web-urls URLS         full ASPNETCORE_URLS for Web (overrides bind/port)

  BFF / admin:
  --api-url URL           Web → API base URL (default: first http URL from API listen)
  --admin-email EMAIL     seed admin when DB has no users
  --admin-password PASS   seed admin password
  --link-into-apps        symlink appsettings.Production.json into api/ and web/
  --force                 overwrite existing files
  -h, --help

Env aliases: SCHNAPP_API_BIND, SCHNAPP_WEB_BIND, SCHNAPP_API_PORT, SCHNAPP_WEB_PORT,
             SCHNAPP_API_URLS, SCHNAPP_WEB_URLS, SCHNAPP_API_URL
EOF
}

need_val() {
  [[ $# -ge 2 ]] || { echo "error: $1 needs a value" >&2; exit 1; }
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    -p|--prefix) need_val "$@"; PREFIX="$2"; shift 2 ;;
    --provider) need_val "$@"; DB_PROVIDER="$2"; shift 2 ;;
    --db-host) need_val "$@"; DB_HOST="$2"; shift 2 ;;
    --db-port) need_val "$@"; DB_PORT="$2"; shift 2 ;;
    --firebird-dir) need_val "$@"; FIREBIRD_DIR="$2"; shift 2 ;;
    --db-name) need_val "$@"; DB_NAME="$2"; shift 2 ;;
    --db-user) need_val "$@"; DB_USER="$2"; shift 2 ;;
    --db-password) need_val "$@"; DB_PASSWORD="$2"; shift 2 ;;
    --blob-root) need_val "$@"; BLOB_ROOT="$2"; shift 2 ;;
    --api-bind) need_val "$@"; API_BIND="$2"; shift 2 ;;
    --web-bind) need_val "$@"; WEB_BIND="$2"; shift 2 ;;
    --api-port) need_val "$@"; API_PORT="$2"; shift 2 ;;
    --web-port) need_val "$@"; WEB_PORT="$2"; shift 2 ;;
    --api-urls) need_val "$@"; API_URLS="$2"; shift 2 ;;
    --web-urls) need_val "$@"; WEB_URLS="$2"; shift 2 ;;
    --api-url) need_val "$@"; API_URL="$2"; shift 2 ;;
    --admin-email) need_val "$@"; ADMIN_EMAIL="$2"; shift 2 ;;
    --admin-password) need_val "$@"; ADMIN_PASSWORD="$2"; shift 2 ;;
    --link-into-apps) LINK_INTO_APPS=1; shift ;;
    --force) FORCE=1; shift ;;
    *) echo "unknown option: $1" >&2; usage; exit 1 ;;
  esac
done

BLOB_ROOT="${BLOB_ROOT:-${PREFIX}/data/blobs}"
FIREBIRD_DIR="${FIREBIRD_DIR:-${PREFIX}/data/firebird}"

case "${DB_PROVIDER,,}" in
  postgres|postgresql)
    DB_PROVIDER_CFG="PostgreSQL"
    DB_PORT="${DB_PORT:-5432}"
    CONN="Host=${DB_HOST};Port=${DB_PORT};Database=${DB_NAME};Username=${DB_USER};Password=${DB_PASSWORD}"
    ;;
  firebird)
    DB_PROVIDER_CFG="Firebird"
    DB_PORT="${DB_PORT:-3050}"
    CONN="DataSource=${DB_HOST};Port=${DB_PORT};Database=${FIREBIRD_DIR}/${DB_NAME}.fdb;User=${DB_USER};Password=${DB_PASSWORD};Charset=UTF8;Dialect=3"
    ;;
  *)
    echo "error: --provider must be postgresql or firebird (got ${DB_PROVIDER})" >&2
    exit 1
    ;;
esac

if [[ -z "$DB_PASSWORD" ]]; then
  echo "error: --db-password or SCHNAPP_DB_PASSWORD required" >&2
  exit 1
fi

# Build ASPNETCORE_URLS from comma-separated binds + port.
# Hosts may be 127.0.0.1, 0.0.0.0, *, ::, [::1], etc.
# IPv6 literals without brackets get brackets for the URL form when they contain ':'.
build_urls() {
  local binds="$1"
  local port="$2"
  local out="" host url
  local IFS=','
  # shellcheck disable=SC2086
  set -- $binds
  for host in "$@"; do
    host="$(echo "$host" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
    [[ -n "$host" ]] || continue
    if [[ "$host" == \[*\] ]]; then
      url="http://${host}:${port}"
    elif [[ "$host" == *:* ]]; then
      url="http://[${host}]:${port}"
    else
      url="http://${host}:${port}"
    fi
    if [[ -z "$out" ]]; then
      out="$url"
    else
      out="${out};${url}"
    fi
  done
  if [[ -z "$out" ]]; then
    echo "error: no listen addresses from binds='${binds}' port='${port}'" >&2
    exit 1
  fi
  printf '%s' "$out"
}

# First http(s) URL in a semicolon list (for default ApiBaseUrl).
first_url() {
  local urls="$1"
  local IFS=';'
  # shellcheck disable=SC2086
  set -- $urls
  printf '%s' "${1:-}"
}

if [[ -z "$API_URLS" ]]; then
  API_URLS="$(build_urls "$API_BIND" "$API_PORT")"
fi
if [[ -z "$WEB_URLS" ]]; then
  WEB_URLS="$(build_urls "$WEB_BIND" "$WEB_PORT")"
fi
if [[ -z "$API_URL" ]]; then
  API_URL="$(first_url "$API_URLS")"
fi

CFG="${PREFIX}/config"
mkdir -p "$CFG"

json_esc() {
  # shellcheck disable=SC2001
  printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'
}

CONN_JSON="$(json_esc "$CONN")"
BLOB_JSON="$(json_esc "$BLOB_ROOT")"
API_URL_JSON="$(json_esc "$API_URL")"
PROVIDER_JSON="$(json_esc "$DB_PROVIDER_CFG")"

write_file() {
  local path="$1"
  local content="$2"
  if [[ -e "$path" && "$FORCE" != "1" ]]; then
    echo "skip (exists): $path  (use --force to overwrite)"
    return
  fi
  printf '%s\n' "$content" > "$path"
  chmod 640 "$path"
  echo "wrote $path"
}

API_JSON=$(cat <<EOF
{
  "ConnectionStrings": {
    "Main": "${CONN_JSON}"
  },
  "Schnapp": {
    "DatabaseProvider": "${PROVIDER_JSON}",
    "BlobRoot": "${BLOB_JSON}",
    "MigrateOnStartup": true
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
EOF
)

WEB_JSON=$(cat <<EOF
{
  "Schnapp": {
    "ApiBaseUrl": "${API_URL_JSON}",
    "ShowDevTools": false
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
EOF
)

API_ENV=$(cat <<EOF
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=${API_URLS}
ConnectionStrings__Main=${CONN}
Schnapp__DatabaseProvider=${DB_PROVIDER_CFG}
Schnapp__BlobRoot=${BLOB_ROOT}
Schnapp__MigrateOnStartup=true
EOF
)
if [[ -n "$ADMIN_EMAIL" ]]; then
  API_ENV+=$'\n'"SCHNAPP_ADMIN_EMAIL=${ADMIN_EMAIL}"
fi
if [[ -n "$ADMIN_PASSWORD" ]]; then
  API_ENV+=$'\n'"SCHNAPP_ADMIN_PASSWORD=${ADMIN_PASSWORD}"
fi

WEB_ENV=$(cat <<EOF
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=${WEB_URLS}
Schnapp__ApiBaseUrl=${API_URL}
EOF
)

write_file "${CFG}/api.appsettings.Production.json" "$API_JSON"
write_file "${CFG}/web.appsettings.Production.json" "$WEB_JSON"
write_file "${CFG}/api.env" "$API_ENV"
write_file "${CFG}/web.env" "$WEB_ENV"

if [[ "$(id -u)" -eq 0 ]] && id schnapp >/dev/null 2>&1; then
  chown -R schnapp:schnapp "$CFG" 2>/dev/null || true
  chmod 750 "$CFG"
fi

if [[ "$LINK_INTO_APPS" == "1" ]]; then
  if [[ -d "${PREFIX}/api" ]]; then
    ln -sfn "${CFG}/api.appsettings.Production.json" \
      "${PREFIX}/api/appsettings.Production.json"
    echo "linked ${PREFIX}/api/appsettings.Production.json"
  fi
  if [[ -d "${PREFIX}/web" ]]; then
    ln -sfn "${CFG}/web.appsettings.Production.json" \
      "${PREFIX}/web/appsettings.Production.json"
    echo "linked ${PREFIX}/web/appsettings.Production.json"
  fi
fi

echo
echo "Config written under ${CFG}/"
echo "  API ASPNETCORE_URLS=${API_URLS}"
echo "  Web ASPNETCORE_URLS=${WEB_URLS}"
echo "  Web Schnapp:ApiBaseUrl=${API_URL}"
echo "  api.env / web.env → systemd EnvironmentFile="
echo "  See docs/deployment-fedora.md for unit files (step 6)."
