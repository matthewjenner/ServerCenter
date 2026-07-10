#!/usr/bin/env bash
#
# Publishes the agent self-contained for a Linux RID and packages an installable tarball
# (binary + systemd unit + env template + install script). The same package installs on the
# hypervisor host (node zero) and on every guest.
#
# Usage:  ./Scripts/publish-agent.sh [linux-x64|linux-arm64]   (default linux-x64)
#
set -euo pipefail

rid="${1:-linux-x64}"
case "$rid" in
    linux-x64|linux-arm64) ;;
    *) echo "usage: $0 [linux-x64|linux-arm64]" >&2; exit 1 ;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(dirname "$script_dir")"
cd "$root"

version=$(grep -oE '<VersionPrefix>[^<]+</VersionPrefix>' Directory.Build.props \
    | sed -E 's|</?VersionPrefix>||g' | tr -d '[:space:]')

out="artifacts/agent/$rid"
stage="$out/stage"
rm -rf "$out"
mkdir -p "$stage/bin"

echo "==> Publishing agent ($rid, self-contained, single-file) v$version"
dotnet publish Src/ServerCenter.Agent/ServerCenter.Agent.csproj \
    --configuration Release \
    --runtime "$rid" \
    --self-contained true \
    -p:PublishSingleFile=true \
    --output "$stage/bin"

echo "==> Staging deployment assets"
cp Deploy/servercenter-agent.service Deploy/servercenter-agent.env Deploy/install.sh Deploy/README.md "$stage/"
chmod +x "$stage/install.sh"

tarball="$out/servercenter-agent-$version-$rid.tar.gz"
tar -czf "$tarball" -C "$stage" .

echo "==> Packaged $tarball"
