#!/bin/sh
# Launch Schnapp.Web with Production env (sourced from quoted web.env).
set -eu
cd /opt/schnapp/web
if [ -f /opt/schnapp/config/web.env ]; then
	set -a
	# shellcheck disable=SC1091
	. /opt/schnapp/config/web.env
	set +a
fi
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
if [ -x ./Schnapp.Web ]; then
	exec ./Schnapp.Web
fi
exec /usr/bin/dotnet ./Schnapp.Web.dll
