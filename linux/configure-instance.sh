#!/usr/bin/env bash
# Administrator-only provisioning. Never starts a service or accepts a secret argument.
set -euo pipefail
[[ $EUID == 0 && $# == 3 ]] || { echo 'Usage: sudo configure-instance.sh NAME TUNNEL_ID DASHBOARD_PORT' >&2; exit 64; }
instance=$1; tunnel=$2; port=$3
[[ $instance =~ ^[a-z][a-z0-9-]{0,31}$ && $tunnel =~ ^tunnel_[A-Za-z0-9]+$ && $port =~ ^[0-9]{4,5}$ ]] || exit 64
(( 10#$port >= 1024 && 10#$port <= 65535 )) || exit 64
config="/etc/devlens/$instance"; state="/var/lib/devlens-$instance"; account="devlens-$instance"
[[ ! -e $config && ! -e $state ]] || { echo 'Instance exists; preserve it and follow upgrade instructions.' >&2; exit 1; }
if id "$account" &>/dev/null; then echo 'User already exists; review it manually.' >&2; exit 1; fi
useradd --system --user-group --no-create-home --shell /usr/sbin/nologin "$account"
install -d -m 0700 -o "$account" -g "$account" "$config" "$state" "$state/profiles"
python3 - "$instance" "$config/instance.json" <<'PY'
import json,sys
with open('/opt/devlens/linux/instance.example.json') as f: config=json.load(f)
config['instanceId']=sys.argv[1]
with open(sys.argv[2],'x') as f: json.dump(config,f,indent=2)
PY
chown "$account:$account" "$config/instance.json"; chmod 0400 "$config/instance.json"
runuser -u "$account" -- /opt/devlens/tunnel/tunnel-client init --sample sample_mcp_stdio_local \
  --profile "$instance" --profile-dir "$state/profiles" --tunnel-id "$tunnel" \
  --control-plane-api-key-ref env:CONTROL_PLANE_API_KEY --health-listen-addr "127.0.0.1:$port" \
  --mcp-command "/opt/devlens/app/SqlServerDevLens.Headless --config $config/instance.json"
echo 'Provisioned. Edit instance.json and create root-only sql-password/tunnel-key files. No service started.'
