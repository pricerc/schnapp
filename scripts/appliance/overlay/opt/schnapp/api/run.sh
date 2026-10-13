#!/bin/sh
# Launch Schnapp.Api with Production env (sourced from quoted api.env).
set -eu
cd /opt/schnapp/api
if [ -f /opt/schnapp/config/api.env ]; then
	set -a
	# shellcheck disable=SC1091
	. /opt/schnapp/config/api.env
	set +a
fi
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
if [ -x ./Schnapp.Api ]; then
	exec ./Schnapp.Api
fi
exec /usr/bin/dotnet ./Schnapp.Api.dll
