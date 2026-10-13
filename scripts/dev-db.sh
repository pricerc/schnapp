#!/usr/bin/env bash
# Ensure host Postgres has role/database for Schnapp dev.
# Uses peer auth as the postgres superuser via the unix socket.
set -euo pipefail

ROLE="${SCHNAPP_DB_USER:-schnapp}"
PASS="${SCHNAPP_DB_PASSWORD:-schnapp}"
DB="${SCHNAPP_DB_NAME:-schnapp}"
HOST="${SCHNAPP_DB_HOST:-127.0.0.1}"
PORT="${SCHNAPP_DB_PORT:-5432}"
SOCKET_DIR="${PGHOST_SOCKET:-/run/postgresql}"

psql_admin() {
  psql -U postgres -h "$SOCKET_DIR" -d postgres "$@"
}

if ! psql_admin -tAc "SELECT 1" >/dev/null 2>&1; then
  echo "Cannot connect as postgres via ${SOCKET_DIR}." >&2
  echo "Start your host Postgres service, then re-run this script." >&2
  exit 1
fi

psql_admin <<SQL
DO \$\$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '${ROLE}') THEN
    CREATE ROLE ${ROLE} LOGIN PASSWORD '${PASS}';
  ELSE
    ALTER ROLE ${ROLE} WITH LOGIN PASSWORD '${PASS}';
  END IF;
END
\$\$;
SQL

if ! psql_admin -tAc "SELECT 1 FROM pg_database WHERE datname='${DB}'" | grep -q 1; then
  psql_admin -c "CREATE DATABASE ${DB} OWNER ${ROLE};"
fi

psql_admin -c "GRANT ALL PRIVILEGES ON DATABASE ${DB} TO ${ROLE};" >/dev/null
psql -U postgres -h "$SOCKET_DIR" -d "$DB" -c "GRANT ALL ON SCHEMA public TO ${ROLE}; ALTER SCHEMA public OWNER TO ${ROLE};" >/dev/null

echo "Postgres ready: ${ROLE}@${HOST}:${PORT}/${DB}"
PGPASSWORD="$PASS" psql -h "$HOST" -p "$PORT" -U "$ROLE" -d "$DB" -c "SELECT current_user, current_database();" >/dev/null
echo "Password auth OK."
