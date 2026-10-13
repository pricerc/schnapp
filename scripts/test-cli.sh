#!/usr/bin/env bash
# Smoke-test Schnapp CLI against a running (or temporary) API.
#
# Usage:
#   ./scripts/test-cli.sh              # ensure DB, start API if needed, run smoke
#   ./scripts/test-cli.sh --no-start   # require API already on SCHNAPP_API
#   ./scripts/test-cli.sh --keep-config
#   SCHNAPP_ADMIN_EMAIL=… SCHNAPP_ADMIN_PASSWORD=… ./scripts/test-cli.sh
#
# Uses scripts/test-image.png for uploads (override with SCHNAPP_CLI_TEST_IMAGE).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if [[ -x /usr/share/dotnet/dotnet ]]; then
  export PATH="/usr/share/dotnet:${PATH}"
fi

API_URL="${SCHNAPP_API:-http://127.0.0.1:5080}"
ADMIN_EMAIL="${SCHNAPP_ADMIN_EMAIL:-admin@example.com}"
ADMIN_PASSWORD="${SCHNAPP_ADMIN_PASSWORD:-change-me}"
IMAGE="${SCHNAPP_CLI_TEST_IMAGE:-$ROOT/scripts/test-image.png}"
START_API=1
KEEP_CONFIG=0

for arg in "$@"; do
  case "$arg" in
    --no-start) START_API=0 ;;
    --keep-config) KEEP_CONFIG=1 ;;
    -h|--help)
      sed -n '2,12p' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
  esac
done

if [[ ! -f "$IMAGE" ]]; then
  echo "Test image not found: $IMAGE" >&2
  exit 1
fi

# Avoid corporate HTTP proxy intercepting localhost (curl + child tools)
export NO_PROXY="${NO_PROXY:-localhost,127.0.0.1,::1}"
export no_proxy="${no_proxy:-localhost,127.0.0.1,::1}"
unset http_proxy https_proxy HTTP_PROXY HTTPS_PROXY ALL_PROXY all_proxy 2>/dev/null || true

# Isolated config/data so we do not clobber the developer's real dirs
export XDG_CONFIG_HOME XDG_DATA_HOME
XDG_CONFIG_HOME="$(mktemp -d /tmp/schnapp-cli-test-config.XXXXXX)"
XDG_DATA_HOME="$(mktemp -d /tmp/schnapp-cli-test-data.XXXXXX)"
mkdir -p "$XDG_CONFIG_HOME/schnapp" "$XDG_DATA_HOME/schnapp"
echo "CLI config: $XDG_CONFIG_HOME/schnapp"
echo "CLI data:   $XDG_DATA_HOME/schnapp"

API_PID=""
cleanup() {
  if [[ -n "${API_PID}" ]]; then
    kill "$API_PID" 2>/dev/null || true
    wait "$API_PID" 2>/dev/null || true
  fi
  if [[ "$KEEP_CONFIG" -eq 0 ]]; then
    if [[ -n "${XDG_CONFIG_HOME:-}" && "$XDG_CONFIG_HOME" == /tmp/schnapp-cli-test-config.* ]]; then
      rm -rf "$XDG_CONFIG_HOME"
    fi
    if [[ -n "${XDG_DATA_HOME:-}" && "$XDG_DATA_HOME" == /tmp/schnapp-cli-test-data.* ]]; then
      rm -rf "$XDG_DATA_HOME"
    fi
  fi
}
trap cleanup EXIT

api_up() {
  curl --noproxy '*' -sf -m 2 "${API_URL}/api/v1/health" >/dev/null 2>&1
}

echo "Building API + CLI…"
dotnet build src/servers/Schnapp.Api -v q
dotnet build src/clients/Schnapp.Client.Cli -v q

API_DLL="$ROOT/src/servers/Schnapp.Api/bin/Debug/net10.0/Schnapp.Api.dll"
API_APPHOST="$ROOT/src/servers/Schnapp.Api/bin/Debug/net10.0/Schnapp.Api"
SCHNAPP_BIN="$ROOT/src/clients/Schnapp.Client.Cli/bin/Debug/net10.0/schnapp"
if [[ -f "$SCHNAPP_BIN" ]]; then
  SCHNAPP=("$SCHNAPP_BIN")
elif [[ -f "$ROOT/src/clients/Schnapp.Client.Cli/bin/Debug/net10.0/schnapp.dll" ]]; then
  SCHNAPP=(dotnet "$ROOT/src/clients/Schnapp.Client.Cli/bin/Debug/net10.0/schnapp.dll")
else
  echo "CLI binary not found after build." >&2
  exit 1
fi

if ! api_up; then
  if [[ "$START_API" -eq 0 ]]; then
    echo "API not reachable at $API_URL (drop --no-start, or start API first)." >&2
    exit 1
  fi
  echo "Ensuring database…"
  ./scripts/dev-db.sh >/dev/null
  echo "Starting API at $API_URL …"
  export SCHNAPP_ADMIN_EMAIL="$ADMIN_EMAIL"
  export SCHNAPP_ADMIN_PASSWORD="$ADMIN_PASSWORD"
  # Prefer built binary with content root = Api project dir for appsettings
  (
    cd "$ROOT/src/servers/Schnapp.Api"
    if [[ -x "$API_APPHOST" ]]; then
      exec "$API_APPHOST" --urls "$API_URL"
    else
      exec dotnet "$API_DLL" --urls "$API_URL"
    fi
  ) > /tmp/schnapp-cli-test-api.log 2>&1 &
  API_PID=$!
  for _ in $(seq 1 60); do
    if api_up; then
      break
    fi
    if ! kill -0 "$API_PID" 2>/dev/null; then
      echo "API process exited. Log:" >&2
      tail -50 /tmp/schnapp-cli-test-api.log >&2 || true
      exit 1
    fi
    sleep 0.25
  done
  if ! api_up; then
    echo "API did not become healthy. Log:" >&2
    tail -50 /tmp/schnapp-cli-test-api.log >&2 || true
    exit 1
  fi
  echo "API up (pid $API_PID)."
else
  echo "Using existing API at $API_URL"
fi

run() {
  echo "+ schnapp $*"
  "${SCHNAPP[@]}" "$@"
}

echo "=== login ==="
run config set server "$API_URL"
run login "$ADMIN_EMAIL" --password "$ADMIN_PASSWORD"
run whoami

echo "=== machine ==="
run machine show
run machine register --name "cli-test-$(date +%s)"
run machine list
run machine show

echo "=== journal ==="
run journal start --now --notes "cli smoke" --tags "cli,smoke"
run journal list
run journal stop

echo "=== upload (mtime default) ==="
run upload "$IMAGE" --tags "cli,smoke" --monitor "cli-monitor" --notes "test-image.png"

echo "=== upload (explicit time today) ==="
run upload "$IMAGE" --at 13:15 --tags cli,demo --monitor monitor1

echo "=== upload (bare keywords) ==="
run upload "$IMAGE" at 14:30 tags bare,keywords monitor bare-mon notes "bare style"

echo "=== whoami still ok ==="
run whoami

echo
echo "CLI smoke passed."
echo "Config was: $XDG_CONFIG_HOME/schnapp"
if [[ "$KEEP_CONFIG" -eq 1 ]]; then
  echo "(kept via --keep-config)"
fi
