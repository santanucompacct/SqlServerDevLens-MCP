#!/usr/bin/env bash
# Run after extracting the verified complete package into root-owned /opt/devlens.
set -euo pipefail
[[ $EUID == 0 ]] || exit 64
[[ -x /usr/bin/getfacl ]] || { echo 'Install the Ubuntu acl package first: sudo apt-get install acl' >&2; exit 1; }
for unit in devlens@.service devlens-tunnel@.service; do
  install -m 0644 "/opt/devlens/linux/$unit" "/etc/systemd/system/$unit"
done
install -m 0755 /opt/devlens/linux/devlens-health.py /usr/local/sbin/devlens-health
systemctl daemon-reload
echo 'Installed system services. Configure each instance, then run enable-instance.sh NAME.'
