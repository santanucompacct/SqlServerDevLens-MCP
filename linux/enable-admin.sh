#!/usr/bin/env bash
# Run as root after installation. No password argument, shell history, or shared credentials.
set -euo pipefail
[[ $EUID == 0 ]] || exit 64
install -d -o root -g root -m 0755 /etc/devlens
install -d -o root -g root -m 0700 /etc/devlens-admin
token=/etc/devlens-admin/admin-token
if [[ ! -e $token ]]; then
  echo 'Enter a new administrator secret (minimum 16 characters). It is not printed or stored in history.'
  umask 077
  systemd-ask-password 'DevLens Linux admin secret' > "$token"
  chmod 0400 "$token"
fi
[[ -f $token && ! -L $token && $(stat -c '%u:%a' "$token") == '0:400' ]] || { echo 'Invalid admin secret file permissions.' >&2; exit 1; }
[[ $(wc -c < "$token") -ge 16 ]] || { echo 'Use an admin secret with at least 16 characters.' >&2; exit 1; }
systemctl enable --now devlens-admin.service
echo 'Admin service enabled. Access from your PC using SSH forwarding: ssh -L 8090:127.0.0.1:8090 USER@VM'
echo 'Then visit http://127.0.0.1:8090 in your PC browser. Do not create an Azure inbound rule for 8090.'
