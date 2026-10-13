#!/usr/bin/env bash
# Publish Schnapp API + Web into the staging area (raw build output only).
#
# Layout (default ARTIFACTS=<repo>/artifacts):
#   artifacts/staging/server/api/
#   artifacts/staging/server/web/
#   artifacts/staging/server/utils/
#   artifacts/staging/server/MANIFEST.txt
#
# Usage (from repo root or from this directory):
#   ./scripts/deployment/publish-server.sh
#   ./scripts/deployment/publish-server.sh --self-contained
#   ./scripts/deployment/publish-server.sh -o /tmp/out -c Release
#
# Next step: assemble install/upgrade kits with build-server-deployment.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
cd "$ROOT"

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
CONFIG="${CONFIG:-Release}"
RID="${RID:-linux-x64}"
ARTIFACTS="${ARTIFACTS:-${OUT_DIR:-$ROOT/artifacts}}"
SELF_CONTAINED="${SELF_CONTAINED:-0}"

usage() {
  sed -n '2,14p' "$0" | sed 's/^# \{0,1\}//'
  cat <<'EOF'

Options:
  -c, --configuration CFG   Build config (default: Release)
  -r, --runtime RID         RID when self-contained (default: linux-x64)
  -o, --output DIR          Artifacts root (default: artifacts/)
                            Writes under DIR/staging/server/
  --self-contained          Bundle .NET runtime (no shared framework on server)
  --framework-dependent     Smaller publish; needs aspnetcore-runtime-10.0 (default)
  -h, --help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help|help) usage; exit 0 ;;
    -c|--configuration)
      [[ $# -ge 2 ]] || { echo "error: $1 needs a value" >&2; exit 1; }
      CONFIG="$2"; shift 2 ;;
    -r|--runtime)
      [[ $# -ge 2 ]] || { echo "error: $1 needs a value" >&2; exit 1; }
      RID="$2"; shift 2 ;;
    -o|--output)
      [[ $# -ge 2 ]] || { echo "error: $1 needs a value" >&2; exit 1; }
      ARTIFACTS="$2"; shift 2 ;;
    --self-contained) SELF_CONTAINED=1; shift ;;
    --framework-dependent) SELF_CONTAINED=0; shift ;;
    *) echo "unknown option: $1" >&2; usage; exit 1 ;;
  esac
done

command -v "$DOTNET" >/dev/null || { echo "dotnet not found (DOTNET=${DOTNET})" >&2; exit 1; }

# Resolve ARTIFACTS to absolute (relative paths are from repo root after cd above).
if [[ "${ARTIFACTS}" != /* ]]; then
  ARTIFACTS="${ROOT}/${ARTIFACTS}"
fi
ARTIFACTS="$(mkdir -p "$ARTIFACTS" && cd "$ARTIFACTS" && pwd)"

STAGING="${ARTIFACTS}/staging/server"
API_OUT="${STAGING}/api"
WEB_OUT="${STAGING}/web"
UTILS_OUT="${STAGING}/utils"

rm -rf "$STAGING"
mkdir -p "$API_OUT" "$WEB_OUT" "$UTILS_OUT"

echo "Publishing API → ${API_OUT}"
if [[ "$SELF_CONTAINED" == "1" ]]; then
  "$DOTNET" publish src/servers/Schnapp.Api/Schnapp.Api.csproj \
    -c "$CONFIG" -r "$RID" --self-contained true \
    -o "$API_OUT"
else
  "$DOTNET" publish src/servers/Schnapp.Api/Schnapp.Api.csproj \
    -c "$CONFIG" --self-contained false \
    -o "$API_OUT"
fi

echo "Publishing Web → ${WEB_OUT}"
if [[ "$SELF_CONTAINED" == "1" ]]; then
  "$DOTNET" publish src/servers/Schnapp.Web/Schnapp.Web.csproj \
    -c "$CONFIG" -r "$RID" --self-contained true \
    -o "$WEB_OUT"
else
  "$DOTNET" publish src/servers/Schnapp.Web/Schnapp.Web.csproj \
    -c "$CONFIG" --self-contained false \
    -o "$WEB_OUT"
fi

echo "Publishing Utils → ${UTILS_OUT}"
if [[ "$SELF_CONTAINED" == "1" ]]; then
  "$DOTNET" publish src/clients/Schnapp.Utils/Schnapp.Utils.csproj \
    -c "$CONFIG" -r "$RID" --self-contained true \
    -o "$UTILS_OUT"
else
  "$DOTNET" publish src/clients/Schnapp.Utils/Schnapp.Utils.csproj \
    -c "$CONFIG" --self-contained false \
    -o "$UTILS_OUT"
fi

{
  echo "Schnapp server publish (staging)"
  echo "date:       $(date -u +%Y-%m-%dT%H:%MZ)"
  echo "config:     $CONFIG"
  echo "self_contained: $SELF_CONTAINED"
  [[ "$SELF_CONTAINED" == "1" ]] && echo "rid:        $RID"
  echo "api_project: src/servers/Schnapp.Api/Schnapp.Api.csproj"
  echo "web_project: src/servers/Schnapp.Web/Schnapp.Web.csproj"
  echo "utils_project: src/clients/Schnapp.Utils/Schnapp.Utils.csproj"
  echo "dotnet:     $("$DOTNET" --version 2>/dev/null || true)"
  echo "staging:    $STAGING"
} > "${STAGING}/MANIFEST.txt"

# Machine-readable path for build-server-deployment.sh (and other callers).
printf '%s\n' "$STAGING" > "${ARTIFACTS}/.last-schnapp-server-publish"

echo
echo "Staged (raw build output):"
echo "  $STAGING"
echo "  $API_OUT"
echo "  $WEB_OUT"
echo "  $UTILS_OUT"
echo
echo "Next: ./scripts/deployment/build-server-deployment.sh"
echo "  → artifacts/publish/server/YYYYMMDD  (full install kit)"
echo "  → artifacts/upgrade/server/YYYYMMDD  (binary upgrade kit)"
