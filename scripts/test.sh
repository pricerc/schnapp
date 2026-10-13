#!/usr/bin/env bash
# Run Schnapp tests. Usage:
#   ./scripts/test.sh              # all tests
#   ./scripts/test.sh unit         # domain + client.core only (no DB)
#   ./scripts/test.sh data         # data/migration tests
#   ./scripts/test.sh api          # api integration tests
#   ./scripts/test.sh --filter X   # pass through to dotnet test
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

# Prefer .NET 10: /tmp/dotnet10 (dev install), then ~/.dotnet, then PATH
if [[ -x /tmp/dotnet10/dotnet ]]; then
  export DOTNET_ROOT=/tmp/dotnet10
  export PATH="/tmp/dotnet10:${PATH}"
elif [[ -x "${HOME}/.dotnet/dotnet" ]] && "${HOME}/.dotnet/dotnet" --list-sdks 2>/dev/null | grep -q '^10\.'; then
  export DOTNET_ROOT="${HOME}/.dotnet"
  export PATH="${DOTNET_ROOT}:${PATH}"
elif [[ -x /usr/share/dotnet/dotnet ]] && /usr/share/dotnet/dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  export DOTNET_ROOT=/usr/share/dotnet
  export PATH="/usr/share/dotnet:${PATH}"
fi

DOTNET="${DOTNET:-dotnet}"
SLN="${SLN:-src/Schnapp.slnx}"

DEFAULT_CS="Host=127.0.0.1;Port=5432;Database=schnapp;Username=schnapp;Password=schnapp"
export SCHNAPP_TEST_CONNECTION="${SCHNAPP_TEST_CONNECTION:-$DEFAULT_CS}"

export NO_PROXY="${NO_PROXY:-localhost,127.0.0.1,::1}"
export no_proxy="${no_proxy:-localhost,127.0.0.1,::1}"

usage() {
  sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//'
}

MODE="${1:-all}"
EXTRA=()

case "$MODE" in
  -h|--help|help)
    usage
    exit 0
    ;;
  unit)
    FILTER='FullyQualifiedName~Schnapp.Domain.Tests|FullyQualifiedName~Schnapp.Client.Core.Tests'
    shift || true
    ;;
  data)
    FILTER='FullyQualifiedName~Schnapp.Data.Tests'
    shift || true
    ;;
  api)
    FILTER='FullyQualifiedName~Schnapp.Api.Tests'
    shift || true
    ;;
  all)
    FILTER=""
    shift || true
    ;;
  --filter)
    FILTER=""
    EXTRA=("$@")
    ;;
  *)
    if [[ "$MODE" == --* ]]; then
      FILTER=""
      EXTRA=("$@")
    else
      FILTER="$MODE"
      shift || true
      EXTRA=("$@")
    fi
    ;;
esac

if [[ $# -gt 0 ]]; then
  EXTRA+=("$@")
fi

ARGS=(test "$SLN" --nologo)
if [[ -n "${FILTER:-}" ]]; then
  ARGS+=(--filter "$FILTER")
fi
ARGS+=("${EXTRA[@]}")

echo "dotnet: $($DOTNET --version 2>/dev/null || echo '?')"
echo "solution: $SLN"
if [[ -n "${FILTER:-}" ]]; then
  echo "filter: $FILTER"
fi
echo "SCHNAPP_TEST_CONNECTION: ${SCHNAPP_TEST_CONNECTION%%;*};…"
echo

exec "$DOTNET" "${ARGS[@]}"
