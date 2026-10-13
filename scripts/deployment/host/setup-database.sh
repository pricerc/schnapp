#!/usr/bin/env bash
# Create PostgreSQL role + database for Schnapp (Fedora / RHEL style).
# Run on the server as a user that can sudo to postgres (or as root).
#
# Usage:
#   sudo ./scripts/deployment/host/setup-database.sh
#   SCHNAPP_DB_PASSWORD='secret' sudo -E ./scripts/deployment/host/setup-database.sh
#   ./scripts/deployment/host/setup-database.sh --dry-run
#
# Env (optional):
#   SCHNAPP_DB_NAME       default: schnapp
#   SCHNAPP_DB_USER       default: schnapp
#   SCHNAPP_DB_PASSWORD   required unless --password-from-stdin
#   SCHNAPP_DB_HOST       default: 127.0.0.1 (info only; role is local)
set -euo pipefail

DB_NAME="${SCHNAPP_DB_NAME:-schnapp}"
DB_USER="${SCHNAPP_DB_USER:-schnapp}"
DB_PASSWORD="${SCHNAPP_DB_PASSWORD:-}"
DRY_RUN=0
PASSWORD_STDIN=0

usage() {
  sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'
  cat <<'EOF'

Options:
  --name NAME           Database name (default: schnapp)
  --user USER           Role name (default: schnapp)
  --password PASS       Role password (or set SCHNAPP_DB_PASSWORD)
  --password-from-stdin Read password from stdin (no echo if tty)
  --dry-run             Print SQL only
  -h, --help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    --name) DB_NAME="$2"; shift 2 ;;
    --user) DB_USER="$2"; shift 2 ;;
    --password) DB_PASSWORD="$2"; shift 2 ;;
    --password-from-stdin) PASSWORD_STDIN=1; shift ;;
    --dry-run) DRY_RUN=1; shift ;;
    *) echo "unknown option: $1" >&2; usage; exit 1 ;;
  esac
done

if [[ "$PASSWORD_STDIN" == "1" ]]; then
  if [[ -t 0 ]]; then
    read -r -s -p "Password for role ${DB_USER}: " DB_PASSWORD
    echo
  else
    DB_PASSWORD="$(cat)"
  fi
fi

if [[ -z "$DB_PASSWORD" ]]; then
  echo "error: set SCHNAPP_DB_PASSWORD or pass --password / --password-from-stdin" >&2
  exit 1
fi

# Escape single quotes for SQL string literals
sql_escape() {
  printf '%s' "$1" | sed "s/'/''/g"
}
PASS_ESC="$(sql_escape "$DB_PASSWORD")"
USER_ESC="$(sql_escape "$DB_USER")"
NAME_ESC="$(sql_escape "$DB_NAME")"

SQL_CREATE_ROLE=$(cat <<SQL
DO \$\$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '${USER_ESC}') THEN
    CREATE ROLE ${DB_USER} LOGIN PASSWORD '${PASS_ESC}';
  ELSE
    ALTER ROLE ${DB_USER} WITH LOGIN PASSWORD '${PASS_ESC}';
  END IF;
END
\$\$;
SQL
)

if [[ "$DRY_RUN" == "1" ]]; then
  echo "$SQL_CREATE_ROLE"
  echo "CREATE DATABASE ${DB_NAME} OWNER ${DB_USER};  -- if not exists (manual check)"
  echo "GRANT ALL PRIVILEGES ON DATABASE ${DB_NAME} TO ${DB_USER};"
  exit 0
fi

run_psql() {
  if [[ "$(id -u)" -eq 0 ]]; then
    sudo -u postgres psql -v ON_ERROR_STOP=1 "$@"
  elif command -v sudo >/dev/null && sudo -n -u postgres true 2>/dev/null; then
    sudo -u postgres psql -v ON_ERROR_STOP=1 "$@"
  elif [[ -n "${PGUSER:-}" ]] || [[ -n "${PGHOST:-}" ]]; then
    psql -v ON_ERROR_STOP=1 "$@"
  else
    # peer auth as current user if superuser
    psql -v ON_ERROR_STOP=1 "$@"
  fi
}

echo "Creating role ${DB_USER} (if needed)…"
echo "$SQL_CREATE_ROLE" | run_psql -d postgres

EXISTS="$(echo "SELECT 1 FROM pg_database WHERE datname = '${NAME_ESC}'" | run_psql -d postgres -tA | tr -d '[:space:]')"
if [[ "$EXISTS" != "1" ]]; then
  echo "Creating database ${DB_NAME}…"
  echo "CREATE DATABASE ${DB_NAME} OWNER ${DB_USER};" | run_psql -d postgres
else
  echo "Database ${DB_NAME} already exists."
fi

echo "GRANT ALL PRIVILEGES ON DATABASE ${DB_NAME} TO ${DB_USER};" | run_psql -d postgres

# Postgres 15+: grant schema usage for app migrations
echo "GRANT ALL ON SCHEMA public TO ${DB_USER};" | run_psql -d "$DB_NAME" 2>/dev/null || true
echo "ALTER DATABASE ${DB_NAME} OWNER TO ${DB_USER};" | run_psql -d postgres 2>/dev/null || true

echo
echo "Done. Connection string template:"
echo "  Host=127.0.0.1;Port=5432;Database=${DB_NAME};Username=${DB_USER};Password=***"
echo
echo "If the API cannot connect, check pg_hba.conf for scram/md5 on 127.0.0.1 and reload postgresql."
