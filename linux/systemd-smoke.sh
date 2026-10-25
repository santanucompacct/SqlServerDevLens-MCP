#!/usr/bin/env bash
# Disposable CI only. Fake tunnel replaces external network activity; real runtime uses synthetic credentials.
set -euo pipefail
[[ $EUID == 0 && ${DEVLENS_DISPOSABLE_CI:-} == 1 ]] || exit 64
trap 'journalctl -u devlens@ci-alpha -u devlens-tunnel@ci-alpha -u devlens@ci-beta -n 100 --no-pager' ERR
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
  ready=false
  for ((attempt=0;attempt<60;attempt++)); do
    if systemctl is-active --quiet "devlens@$instance" && systemctl is-active --quiet "devlens-tunnel@$instance"; then ready=true; break; fi
    sleep 1
  done
  [[ $ready == true ]]
  systemctl is-enabled --quiet "devlens@$instance" "devlens-tunnel@$instance"
  systemctl is-active --quiet "devlens@$instance" "devlens-tunnel@$instance"
  [[ -S /run/devlens-$instance/mcp.sock ]]
done
# PartOf= on the tunnel causes an ordered stop/start after a runtime crash.
# Observe both services stable before deliberately crashing the next process.
wait_pair() {
  for ((i=0;i<90;i++)); do
    if systemctl is-active --quiet devlens@ci-alpha devlens-tunnel@ci-alpha; then
      r=$(systemctl show -p MainPID --value devlens@ci-alpha)
      t=$(systemctl show -p MainPID --value devlens-tunnel@ci-alpha)
      if [[ $r =~ ^[0-9]+$ && $r -gt 1 && $t =~ ^[0-9]+$ && $t -gt 1 ]]; then
        sleep 2
        if systemctl is-active --quiet devlens@ci-alpha devlens-tunnel@ci-alpha; then return 0; fi
      fi
    fi
    sleep 1
  done
  echo "Timed out waiting for both DevLens services to stabilize" >&2
  return 1
}
# Crash either process independently; wait for automatic restart without logging into its user.
for service in devlens@ci-alpha devlens-tunnel@ci-alpha; do
  wait_pair
  before=$(systemctl show -p MainPID --value "$service")
  [[ $before =~ ^[0-9]+$ && $before -gt 1 ]]
  kill -KILL "$before"
  recovered=false
  for ((attempt=0;attempt<60;attempt++)); do
    after=$(systemctl show -p MainPID --value "$service")
    if [[ $after != 0 && $after != "$before" ]] && systemctl is-active --quiet "$service"; then recovered=true; break; fi
    sleep 1
  done
  [[ $recovered == true ]]
  wait_pair
done
for instance in ci-alpha ci-beta; do
  systemctl stop "devlens@$instance"
  ! systemctl is-active --quiet "devlens-tunnel@$instance"
  [[ ! -e /run/devlens-$instance/mcp.sock ]]
  [[ -f /etc/devlens/$instance/sql-password && -f /etc/devlens/$instance/instance.json ]]
  timeout 120 systemctl start "devlens@$instance" "devlens-tunnel@$instance"
  systemctl is-active --quiet "devlens@$instance" "devlens-tunnel@$instance"
  systemctl disable --now "devlens@$instance" "devlens-tunnel@$instance"
done
echo 'PASS: systemd unattended credentials, enabled boot units, separate identities, crash recovery, ordered stop/start and persistent configuration (offline tunnel fixture)'
