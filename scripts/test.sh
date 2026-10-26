#!/usr/bin/env bash
# Run Schnapp tests. Usage:
#   ./scripts/test.sh              # all tests
#   ./scripts/test.sh unit         # domain + client.core only (no DB)
#   ./scripts/test.sh data         # data/migration tests
#   ./scripts/test.sh api          # api integration tests
#   ./scripts/test.sh --project FILE --filter X
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
PROJECTS=()

case "$MODE" in
  -h|--help|help)
    usage
    exit 0
    ;;
  unit)
    PROJECTS=(
      tests/Schnapp.Domain.Tests/Schnapp.Domain.Tests.csproj
      tests/Schnapp.Client.Core.Tests/Schnapp.Client.Core.Tests.csproj
    )
    shift || true
    ;;
  data)
    PROJECTS=(tests/Schnapp.Data.Tests/Schnapp.Data.Tests.csproj)
    shift || true
    ;;
  api)
    PROJECTS=(tests/Schnapp.Api.Tests/Schnapp.Api.Tests.csproj)
    shift || true
    ;;
  all)
    shift || true
    ;;
  --project)
    [[ $# -ge 2 ]] || { echo "error: --project needs a project file" >&2; exit 1; }
    PROJECTS=("$2")
    shift 2
    ;;
  --filter)
    echo "error: --filter requires selecting one project with --project" >&2
    exit 1
    ;;
  *)
    if [[ "$MODE" == --* ]]; then
      EXTRA=("$@")
    else
      echo "error: unknown mode '$MODE' (expected all, unit, data, api, or --project)" >&2
      exit 2
    fi
    ;;
esac

if [[ $# -gt 0 ]]; then
  EXTRA+=("$@")
fi

echo "dotnet: $($DOTNET --version 2>/dev/null || echo '?')"
echo "solution: $SLN"
echo "SCHNAPP_TEST_CONNECTION: ${SCHNAPP_TEST_CONNECTION%%;*};…"
echo

if [[ ${#PROJECTS[@]} -eq 0 ]]; then
  exec "$DOTNET" test --solution "$SLN" "${EXTRA[@]}"
fi

for project in "${PROJECTS[@]}"; do
  args=(test --project "$project" -p:Platform=x64)
  args+=("${EXTRA[@]}")
  "$DOTNET" "${args[@]}"
done
