#!/usr/bin/env bash
# Disposable CI only. Fake tunnel replaces external network activity; real runtime uses synthetic credentials.
set -euo pipefail
[[ $EUID == 0 && ${DEVLENS_DISPOSABLE_CI:-} == 1 ]] || exit 64
bash /opt/devlens/linux/install-services.sh
for instance in ci-alpha ci-beta; do
  for secret in sql-password tunnel-key; do
    printf '%s' synthetic-offline-secret > "/etc/devlens/$instance/$secret"
    chmod 0400 "/etc/devlens/$instance/$secret"
  done
  install -d "/run/systemd/system/devlens-tunnel@$instance.service.d"
  printf '[Service]\nExecStart=\nExecStart=/bin/sleep infinity\n' > "/run/systemd/system/devlens-tunnel@$instance.service.d/offline.conf"
done
systemctl daemon-reload
for instance in ci-alpha ci-beta; do
  bash /opt/devlens/linux/enable-instance.sh "$instance"
  systemctl is-enabled --quiet "devlens@$instance" "devlens-tunnel@$instance"
  systemctl is-active --quiet "devlens@$instance" "devlens-tunnel@$instance"
  [[ -S /run/devlens-$instance/mcp.sock ]]
done
# Crash either process independently; wait for automatic restart without logging into its user.
for service in devlens@ci-alpha devlens-tunnel@ci-alpha; do
  before=$(systemctl show -p MainPID --value "$service")
  kill -KILL "$before"
  recovered=false
  for ((attempt=0;attempt<60;attempt++)); do
    after=$(systemctl show -p MainPID --value "$service")
    if [[ $after != 0 && $after != "$before" ]] && systemctl is-active --quiet "$service"; then recovered=true; break; fi
    sleep 1
  done
  [[ $recovered == true ]]
done
for instance in ci-alpha ci-beta; do
  systemctl stop "devlens@$instance"
  ! systemctl is-active --quiet "devlens-tunnel@$instance"
  [[ ! -e /run/devlens-$instance/mcp.sock ]]
  [[ -f /etc/devlens/$instance/sql-password && -f /etc/devlens/$instance/instance.json ]]
  systemctl start "devlens@$instance" "devlens-tunnel@$instance"
  systemctl is-active --quiet "devlens@$instance" "devlens-tunnel@$instance"
  systemctl disable --now "devlens@$instance" "devlens-tunnel@$instance"
done
echo 'PASS: systemd unattended credentials, enabled boot units, separate identities, crash recovery, ordered stop/start and persistent configuration (offline tunnel fixture)'
