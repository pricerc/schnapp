#!/usr/bin/env bash
# Create a Firebird 5 SuperServer user + empty schnapp.fdb (Fedora / RHEL style).
# Run on the server as root (or a user that can talk to Firebird as SYSDBA).
#
# Usage:
#   sudo SCHNAPP_DB_PASSWORD='secret' ./scripts/deployment/host/setup-database-firebird.sh
#   ./scripts/deployment/host/setup-database-firebird.sh --dry-run
#
# Env (optional):
#   SCHNAPP_DB_NAME       default: schnapp (file stem)
#   SCHNAPP_DB_USER       default: schnapp
#   SCHNAPP_DB_PASSWORD   required
#   SCHNAPP_DB_HOST       default: 127.0.0.1
#   SCHNAPP_DB_PORT       default: 3050
#   SCHNAPP_FIREBIRD_DIR  default: /opt/schnapp/data/firebird
set -euo pipefail

DB_NAME="${SCHNAPP_DB_NAME:-schnapp}"
DB_USER="${SCHNAPP_DB_USER:-schnapp}"
DB_PASSWORD="${SCHNAPP_DB_PASSWORD:-}"
DB_HOST="${SCHNAPP_DB_HOST:-127.0.0.1}"
DB_PORT="${SCHNAPP_DB_PORT:-3050}"
FB_DIR="${SCHNAPP_FIREBIRD_DIR:-/opt/schnapp/data/firebird}"
SYSDBA_PASSWORD="${FIREBIRD_SYSDBA_PASSWORD:-masterkey}"
DRY_RUN=0

usage() {
  sed -n '2,16p' "$0" | sed 's/^# \{0,1\}//'
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    --name) DB_NAME="$2"; shift 2 ;;
    --user) DB_USER="$2"; shift 2 ;;
    --password) DB_PASSWORD="$2"; shift 2 ;;
    --host) DB_HOST="$2"; shift 2 ;;
    --port) DB_PORT="$2"; shift 2 ;;
    --dir) FB_DIR="$2"; shift 2 ;;
    --dry-run) DRY_RUN=1; shift ;;
    *) echo "unknown option: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ -z "$DB_PASSWORD" ]]; then
  echo "error: set SCHNAPP_DB_PASSWORD or pass --password" >&2
  exit 1
fi

FDB="${FB_DIR}/${DB_NAME}.fdb"
sql_escape() { printf '%s' "$1" | sed "s/'/''/g"; }
USER_ESC="$(sql_escape "$DB_USER")"
PASS_ESC="$(sql_escape "$DB_PASSWORD")"
FDB_ESC="$(sql_escape "$FDB")"

ISQL="${ISQL:-}"
if [[ -z "$ISQL" ]]; then
  for c in isql-fb isql firebird; do
    if command -v "$c" >/dev/null 2>&1; then ISQL="$c"; break; fi
  done
fi

run_isql() {
  if [[ -z "$ISQL" ]]; then
    echo "error: isql-fb/isql not found" >&2
    exit 1
  fi
  "$ISQL" -user SYSDBA -password "$SYSDBA_PASSWORD" "$@"
}

SQL=$(cat <<SQL
CREATE USER ${USER_ESC} PASSWORD '${PASS_ESC}';
COMMIT;
CREATE DATABASE '${FDB_ESC}' USER '${USER_ESC}' PASSWORD '${PASS_ESC}' PAGE_SIZE 16384 DEFAULT CHARACTER SET UTF8;
SQL
)

if [[ "$DRY_RUN" == "1" ]]; then
  echo "mkdir -p ${FB_DIR}"
  echo "$SQL"
  echo "Connection string:"
  echo "  DataSource=${DB_HOST};Port=${DB_PORT};Database=${FDB};User=${DB_USER};Password=***;Charset=UTF8;Dialect=3"
  exit 0
fi

mkdir -p "$FB_DIR"
if [[ -e "$FDB" ]]; then
  echo "Database file ${FDB} already exists."
else
  echo "Creating ${FDB}…"
  # CREATE USER may already exist; CREATE DATABASE is the important step.
  echo "CREATE USER ${USER_ESC} PASSWORD '${PASS_ESC}';" | run_isql || true
  echo "CREATE DATABASE '${FDB_ESC}' USER '${USER_ESC}' PASSWORD '${PASS_ESC}' PAGE_SIZE 16384 DEFAULT CHARACTER SET UTF8;" | run_isql
fi

echo
echo "Done. Connection string template:"
echo "  DataSource=${DB_HOST};Port=${DB_PORT};Database=${FDB};User=${DB_USER};Password=***;Charset=UTF8;Dialect=3"
echo "Set Schnapp:DatabaseProvider=Firebird (or Schnapp__DatabaseProvider=Firebird)."
echo "Change the SYSDBA password from masterkey if it is still the default."
