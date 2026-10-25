#!/usr/bin/env bash
set -euo pipefail
[[ $# == 1 && $1 =~ ^[a-z][a-z0-9-]{0,31}$ ]] || exit 64
instance=$1
[[ $(id -un) == "devlens-$instance" ]] || { echo 'Incorrect instance user' >&2; exit 1; }
[[ -n ${CREDENTIALS_DIRECTORY:-} && -r $CREDENTIALS_DIRECTORY/tunnel-key ]] || exit 1
export CONTROL_PLANE_API_KEY
CONTROL_PLANE_API_KEY=$(cat "$CREDENTIALS_DIRECTORY/tunnel-key")
[[ -n $CONTROL_PLANE_API_KEY ]] || exit 1
exec /opt/devlens/tunnel/tunnel-client run --profile "$instance" --profile-dir "/var/lib/devlens-$instance/profiles"
