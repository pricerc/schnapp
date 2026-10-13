#!/usr/bin/env bash
# Audit / fix Schnapp client SQLite C/P/T hierarchy (ref_* tables).
#
# Usage:
#   ./scripts/fix-cpt-hierarchy-sqlite.sh                 # report default db
#   ./scripts/fix-cpt-hierarchy-sqlite.sh --apply         # fix default db
#   ./scripts/fix-cpt-hierarchy-sqlite.sh /path/db        # report that file
#   ./scripts/fix-cpt-hierarchy-sqlite.sh --apply /path/db
#
# Default DB: $SCHNAPP_DB or ~/.local/share/schnapp/schnapp.db
# Also checks legacy ~/.local/share/schnapp/reference.db if present and different.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REPORT_SQL="$ROOT/scripts/fix-cpt-hierarchy-sqlite.sql"
APPLY_SQL="$ROOT/scripts/fix-cpt-hierarchy-sqlite-apply.sql"

APPLY=0
DBS=()

for arg in "$@"; do
  case "$arg" in
    --apply|-a) APPLY=1 ;;
    --help|-h)
      sed -n '2,14p' "$0"
      exit 0
      ;;
    *) DBS+=("$arg") ;;
  esac
done

if [[ ${#DBS[@]} -eq 0 ]]; then
  DEFAULT="${SCHNAPP_DB:-$HOME/.local/share/schnapp/schnapp.db}"
  DBS=("$DEFAULT")
  LEGACY="$HOME/.local/share/schnapp/reference.db"
  if [[ -f "$LEGACY" && "$(readlink -f "$LEGACY" 2>/dev/null || echo "$LEGACY")" != "$(readlink -f "$DEFAULT" 2>/dev/null || echo "$DEFAULT")" ]]; then
    DBS+=("$LEGACY")
  fi
fi

if ! command -v sqlite3 >/dev/null 2>&1; then
  echo "sqlite3 CLI not found. Install sqlite or use:  apt/pacman/… sqlite" >&2
  exit 1
fi

for DB in "${DBS[@]}"; do
  if [[ ! -f "$DB" ]]; then
    echo "Skip (missing): $DB"
    continue
  fi

  # Need ref_* tables
  if ! sqlite3 "$DB" "SELECT 1 FROM sqlite_master WHERE type='table' AND name='ref_task';" | grep -q 1; then
    echo "Skip (no ref_task table): $DB"
    continue
  fi

  echo
  echo "######################################################################"
  echo "# Database: $DB"
  echo "######################################################################"

  if [[ "$APPLY" -eq 1 ]]; then
    BAK="${DB}.bak-$(date +%Y%m%d-%H%M%S)"
    cp -a "$DB" "$BAK"
    # Also copy WAL/SHM if present so restore is consistent
    [[ -f "${DB}-wal" ]] && cp -a "${DB}-wal" "${BAK}-wal" || true
    [[ -f "${DB}-shm" ]] && cp -a "${DB}-shm" "${BAK}-shm" || true
    echo "Backup: $BAK"
    # Checkpoint WAL into main file before/after for a clean snapshot
    sqlite3 "$DB" "PRAGMA wal_checkpoint(TRUNCATE);" >/dev/null 2>&1 || true
  fi

  sqlite3 "$DB" < "$REPORT_SQL"

  if [[ "$APPLY" -eq 1 ]]; then
    echo
    echo "*** APPLYING fixes ***"
    sqlite3 "$DB" < "$APPLY_SQL"
    sqlite3 "$DB" "PRAGMA wal_checkpoint(TRUNCATE);" >/dev/null 2>&1 || true
    echo
    echo "Re-report after apply:"
    sqlite3 "$DB" < "$REPORT_SQL"
  else
    echo
    echo "Report only. To apply:  $0 --apply \"$DB\""
  fi
done
