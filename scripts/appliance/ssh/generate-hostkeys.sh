#!/usr/bin/env bash
# Generate persistent SSH host keys for the Schnapp appliance image.
# Run once; rebuilds will embed the same fingerprints.
set -euo pipefail
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/hostkeys"
mkdir -p "$DIR"
cd "$DIR"

gen() {
  local type="$1" file="$2"
  shift 2
  if [[ -f "$file" ]]; then
    echo "exists: $file"
    return 0
  fi
  ssh-keygen -t "$type" -f "$file" -N '' -C 'schnapp-appliance-host' "$@"
  echo "created: $file"
}

gen ed25519 ssh_host_ed25519_key
gen rsa ssh_host_rsa_key -b 3072

echo
echo "Host key fingerprints (clients will see these):"
for pub in *.pub; do
  ssh-keygen -lf "$pub"
done
echo
echo "Rebuild the system image, then once on your laptop:"
echo "  ssh-keygen -R schnapp"
echo "  ssh-keygen -R <appliance-ip>"
