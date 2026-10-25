#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
[[ $(uname -s) == Linux && $(uname -m) == x86_64 ]] || exit 1
out=$(mktemp -d); trap 'rm -rf -- "$out"' EXIT
package="$out/SqlServerDevLens-linux-headless-0.1.0-linux-x64"
mkdir -p "$package/app" "$package/tunnel" "$package/linux" distribution/packages/linux-headless
dotnet publish SqlServerDevLens.Headless/SqlServerDevLens.Headless.csproj -c Release -r linux-x64 --self-contained true -o "$package/app"
# Separate Linux pin; existing Windows vendor lock and packaging remain untouched.
curl --fail --location --silent --show-error https://github.com/openai/tunnel-client/releases/download/v0.0.16/tunnel-client-v0.0.16-linux-amd64.zip -o "$out/tunnel.zip"
echo "d60cdba019bce451bcc3a15478cc5b9cb11270b049f5b56ea39a80b517f8b117  $out/tunnel.zip" | sha256sum --check
unzip -q "$out/tunnel.zip" -d "$package/tunnel"
client=$(find "$package/tunnel" -type f -name tunnel-client -print -quit)
[[ -n $client ]] || exit 1
if [[ $client != "$package/tunnel/tunnel-client" ]]; then cp "$client" "$package/tunnel/tunnel-client"; fi
chmod 0755 "$package/tunnel/tunnel-client" "$package/app/SqlServerDevLens.Headless"
cp linux/configure-instance.sh linux/start-tunnel.sh linux/instance.example.json linux/devlens@.service "$package/linux/"
cp docs/LINUX-HEADLESS.md "$package/README.md"
cp LICENSE "$package/LICENSE" 2>/dev/null || cp LICENSE.txt "$package/LICENSE"
dotnet list SqlServerDevLens.Headless/SqlServerDevLens.Headless.csproj package --include-transitive > "$package/DEPENDENCIES.txt"
printf '{"product":"linux-headless-read-only","version":"0.1.0","commit":"%s","privateIdentityIncluded":false}\n' "$(git rev-parse HEAD)" > "$package/BUILD-INFO.json"
chmod 0755 "$package/linux/"*.sh
python3 linux/protocol-smoke.py "$package/app/SqlServerDevLens.Headless"
! find "$package/app" -iname '*Avalonia*' | grep -q .
tar -C "$out" -czf distribution/packages/linux-headless/SqlServerDevLens-linux-headless-0.1.0-linux-x64.tar.gz "$(basename "$package")"
mkdir -p "$out/fresh extraction with spaces"
tar -C "$out/fresh extraction with spaces" -xzf distribution/packages/linux-headless/SqlServerDevLens-linux-headless-0.1.0-linux-x64.tar.gz
python3 linux/protocol-smoke.py "$out/fresh extraction with spaces/$(basename "$package")/app/SqlServerDevLens.Headless"
(cd distribution/packages/linux-headless && sha256sum ./*.tar.gz > SHA256SUMS.txt)
