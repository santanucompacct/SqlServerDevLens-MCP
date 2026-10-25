#!/usr/bin/env bash
# One-time activation. No secrets accepted in arguments or printed.
set -euo pipefail
[[ $EUID == 0 && $# == 1 && $1 =~ ^[a-z][a-z0-9-]{0,31}$ ]] || exit 64
instance=$1
for credential in sql-password tunnel-key; do
  file="/etc/devlens/$instance/$credential"
  [[ -f $file && ! -L $file && -s $file && $(stat -c '%u:%a' "$file") == '0:400' ]] || {
    echo 'Create nonempty root-owned 0400 SQL/tunnel credential files before enabling.' >&2; exit 1;
  }
done
[[ $(stat -c '%u:%a' "/etc/devlens/$instance") == '0:750' ]] || exit 1
[[ -f /etc/devlens/$instance/dashboard-port ]] || exit 1
systemctl enable "devlens@$instance.service" "devlens-tunnel@$instance.service"
systemctl start "devlens@$instance.service" "devlens-tunnel@$instance.service"
echo 'Enabled both services for unattended boot. Run sudo devlens-health to verify live SQL and tunnel polling.'
