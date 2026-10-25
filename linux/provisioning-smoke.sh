#!/usr/bin/env bash
# Disposable CI only; creates profiles with synthetic IDs but never starts tunnels/services.
set -euo pipefail
[[ $EUID == 0 && ${DEVLENS_DISPOSABLE_CI:-} == 1 && $# == 1 ]] || exit 64
[[ ! -e /etc/devlens/ci-alpha && ! -e /etc/devlens/ci-beta ]] || exit 1
install -d -m 0755 /opt/devlens
tar -C /opt/devlens --strip-components=1 -xzf "$1"
bash /opt/devlens/linux/configure-instance.sh ci-alpha tunnel_00000000000000000000000000000001 18080
bash /opt/devlens/linux/configure-instance.sh ci-beta tunnel_00000000000000000000000000000002 18081
python3 - <<'PY'
import pathlib
for name,port in [('ci-alpha',18080),('ci-beta',18081)]:
    profile=pathlib.Path(f'/var/lib/devlens-{name}/profiles/{name}.yaml').read_text()
    assert f'127.0.0.1:{port}' in profile
    assert f'/etc/devlens/{name}/instance.json' in profile
    assert '/opt/devlens/app/SqlServerDevLens.Headless' in profile
    assert 'env:CONTROL_PLANE_API_KEY' in profile
    assert '--desktop' not in profile
    assert pathlib.Path(f'/etc/devlens/{name}/instance.json').stat().st_mode & 0o777 == 0o400
print('PASS: two dedicated users, distinct loopback ports and instance-specific MCP profiles; no services started')
PY
if runuser -u devlens-ci-beta -- test -r /etc/devlens/ci-alpha/instance.json; then exit 1; fi
if runuser -u devlens-ci-alpha -- test -r /var/lib/devlens-ci-beta/profiles/ci-beta.yaml; then exit 1; fi
