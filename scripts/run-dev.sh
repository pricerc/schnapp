#!/usr/bin/env bash
# Start API + Web for manual Phase testing (two processes).
# Usage: ./scripts/run-dev.sh
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if [[ -x /usr/share/dotnet/dotnet ]]; then
  export PATH="/usr/share/dotnet:${PATH}"
fi

./scripts/dev-db.sh

export SCHNAPP_ADMIN_EMAIL="${SCHNAPP_ADMIN_EMAIL:-admin@example.com}"
export SCHNAPP_ADMIN_PASSWORD="${SCHNAPP_ADMIN_PASSWORD:-change-me}"

echo "Admin bootstrap (if empty DB): $SCHNAPP_ADMIN_EMAIL / $SCHNAPP_ADMIN_PASSWORD"
echo "API  → http://127.0.0.1:5080"
echo "Web  → http://127.0.0.1:5081"
echo

# Run API in background, Web in foreground
dotnet run --project src/servers/Schnapp.Api --urls http://127.0.0.1:5080 &
API_PID=$!
trap 'kill $API_PID 2>/dev/null || true' EXIT

# Wait for health
for i in $(seq 1 40); do
  if curl --noproxy '*' -sf http://127.0.0.1:5080/api/v1/health >/dev/null 2>&1; then
    break
  fi
  sleep 0.25
done

dotnet run --project src/servers/Schnapp.Web --urls http://127.0.0.1:5081
