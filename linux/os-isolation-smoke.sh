#!/usr/bin/env bash
# Disposable CI host only. Does not start a tunnel or open SQL.
set -euo pipefail
[[ $EUID == 0 && ${DEVLENS_DISPOSABLE_CI:-} == 1 ]] || exit 64
first="dvci-a-$$"; second="dvci-b-$$"; temporary=$(mktemp -d)
cleanup() {
  userdel "$first" 2>/dev/null || true
  userdel "$second" 2>/dev/null || true
  rm -rf -- "$temporary"
}
trap cleanup EXIT
useradd --system --no-create-home "$first"
useradd --system --no-create-home "$second"
chmod 0755 "$temporary"
for account in "$first" "$second"; do
  install -d -m 0700 -o "$account" "$temporary/$account"
  printf '%s' 'synthetic-not-a-secret' > "$temporary/$account/sql-password"
  chown "$account" "$temporary/$account/sql-password"
  chmod 0400 "$temporary/$account/sql-password"
  runuser -u "$account" -- test -r "$temporary/$account/sql-password"
done
if runuser -u "$second" -- test -r "$temporary/$first/sql-password"; then exit 1; fi
if runuser -u "$first" -- test -r "$temporary/$second/sql-password"; then exit 1; fi
echo 'PASS: separate Linux users cannot read the other instance credential directory'
