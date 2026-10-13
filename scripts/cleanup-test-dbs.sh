#!/usr/bin/env bash
# Drop leftover integration-test databases: schnapp_t_<hex>
# Created by tests/Schnapp.Api.Tests/PostgresTestSupport when SCHNAPP_TEST_CONNECTION is set.
#
# Usage:
#   ./scripts/cleanup-test-dbs.sh           # list + drop all schnapp_t_*
#   ./scripts/cleanup-test-dbs.sh --dry-run # list only
#   ./scripts/cleanup-test-dbs.sh --yes     # drop without confirm when not a TTY
set -euo pipefail

DRY_RUN=0
ASSUME_YES=0
for arg in "$@"; do
  case "$arg" in
    -n|--dry-run) DRY_RUN=1 ;;
    -y|--yes) ASSUME_YES=1 ;;
    -h|--help|help)
      sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *)
      echo "Unknown option: $arg" >&2
      exit 2
      ;;
  esac
done

SOCKET_DIR="${PGHOST_SOCKET:-/run/postgresql}"
PREFIX="${SCHNAPP_TEST_DB_PREFIX:-schnapp_t_}"

psql_admin() {
  if psql -U postgres -h "$SOCKET_DIR" -d postgres -tAc "SELECT 1" >/dev/null 2>&1; then
    psql -U postgres -h "$SOCKET_DIR" -d postgres "$@"
    return
  fi
  # Fallback: SCHNAPP_TEST_CONNECTION / default dev credentials (needs CREATEDB + drop rights)
  local cs="${SCHNAPP_TEST_CONNECTION:-Host=127.0.0.1;Port=5432;Database=postgres;Username=schnapp;Password=schnapp}"
  local host port user pass
  host="$(echo "$cs" | tr ';' '\n' | awk -F= 'tolower($1)=="host"{print $2}')"
  port="$(echo "$cs" | tr ';' '\n' | awk -F= 'tolower($1)=="port"{print $2}')"
  user="$(echo "$cs" | tr ';' '\n' | awk -F= 'tolower($1)=="username"{print $2}')"
  pass="$(echo "$cs" | tr ';' '\n' | awk -F= 'tolower($1)=="password"{print $2}')"
  host="${host:-127.0.0.1}"
  port="${port:-5432}"
  user="${user:-schnapp}"
  pass="${pass:-schnapp}"
  PGPASSWORD="$pass" psql -h "$host" -p "$port" -U "$user" -d postgres "$@"
}

mapfile -t DBS < <(psql_admin -tAc "SELECT datname FROM pg_database WHERE datname LIKE '${PREFIX}%' ORDER BY datname;")

if [[ ${#DBS[@]} -eq 0 || -z "${DBS[0]:-}" ]]; then
  echo "No databases matching '${PREFIX}%'."
  exit 0
fi

echo "Found ${#DBS[@]} database(s) matching '${PREFIX}%':"
printf '  %s\n' "${DBS[@]}"

if [[ "$DRY_RUN" -eq 1 ]]; then
  echo "(dry-run: nothing dropped)"
  exit 0
fi

if [[ "$ASSUME_YES" -ne 1 && -t 0 ]]; then
  read -r -p "Drop all of the above? [y/N] " ans
  case "$ans" in
    y|Y|yes|YES) ;;
    *) echo "Aborted."; exit 1 ;;
  esac
fi

dropped=0
failed=0
for db in "${DBS[@]}"; do
  [[ -z "$db" ]] && continue
  # DROP DATABASE cannot run inside a transaction; psql -c multi-statement is one txn.
  # Terminate and drop must be separate -c invocations.
  psql_admin -v ON_ERROR_STOP=1 -c "
    SELECT pg_terminate_backend(pid)
    FROM pg_stat_activity
    WHERE datname = '${db}' AND pid <> pg_backend_pid();
  " >/dev/null 2>&1 || true

  if psql_admin -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS \"${db}\" WITH (FORCE);" >/dev/null 2>&1 \
  || psql_admin -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS \"${db}\";" >/dev/null; then
    echo "dropped ${db}"
    dropped=$((dropped + 1))
  else
    echo "FAILED  ${db}" >&2
    failed=$((failed + 1))
  fi
done

echo "Done: dropped=${dropped} failed=${failed}"
[[ "$failed" -eq 0 ]]
