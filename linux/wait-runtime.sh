#!/usr/bin/env bash
set -euo pipefail
[[ $# == 1 && $1 =~ ^[a-z][a-z0-9-]{0,31}$ ]] || exit 64
for ((attempt=0; attempt<30; attempt++)); do
  [[ -S /run/devlens-$1/mcp.sock ]] && exit 0
  sleep 1
done
echo 'Private MCP runtime not ready; systemd will retry.' >&2
exit 1
