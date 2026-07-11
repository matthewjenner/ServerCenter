#!/usr/bin/env bash
#
# ServerCenter agent self-update. Asks the CONTROLLER what agent version it serves; if that differs
# from the installed version, downloads that bundle FROM THE CONTROLLER (never GitHub), swaps the
# binary + unit, and restarts. Run as root by servercenter-agent-update.timer.
#
# No rollback yet: this is the simple in-place swap. Blue-green + watchdog is the Phase 10 hardening.
#
set -euo pipefail

env_file=/etc/servercenter-agent/agent.env
install_dir=/opt/servercenter-agent
version_file="$install_dir/VERSION"

[[ -f "$env_file" ]] || { echo "no $env_file; agent not installed" >&2; exit 0; }

controller=$(grep -E '^SERVERCENTER_CONTROLLER=' "$env_file" | head -n1 | cut -d= -f2- | tr -d '[:space:]')
[[ -n "$controller" ]] || { echo "no SERVERCENTER_CONTROLLER in $env_file" >&2; exit 0; }

case "$(uname -m)" in
    x86_64)  rid=linux-x64 ;;
    aarch64) rid=linux-arm64 ;;
    *) echo "unsupported architecture $(uname -m)" >&2; exit 0 ;;
esac

# The plaintext controller speaks h2c, so http needs --http2-prior-knowledge. (mTLS/https auto-update
# also needs the agent's client cert - not wired yet; see the runbook Known gaps.)
curl_opts=(--fail --silent --show-error --location)
case "$controller" in
    http://*) curl_opts+=(--http2-prior-knowledge) ;;
esac

remote=$(curl "${curl_opts[@]}" "$controller/agent/version" | sed -E 's/.*"[vV]ersion"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/')
[[ -n "$remote" ]] || { echo "could not read the controller's agent version" >&2; exit 1; }

local_version=""
[[ -f "$version_file" ]] && local_version=$(tr -d '[:space:]' < "$version_file")

if [[ "$remote" == "$local_version" ]]; then
    echo "agent up to date ($local_version)"
    exit 0
fi

echo "updating agent $local_version -> $remote (from $controller)"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

curl "${curl_opts[@]}" -o "$tmp/agent.tar.gz" "$controller/agent/bundle/$rid"
tar -xzf "$tmp/agent.tar.gz" -C "$tmp"

# Swap the binary + unit + updater assets; keep config and the enrolled identity, then restart.
cp -f "$tmp/bin/"* "$install_dir/"
chmod +x "$install_dir/ServerCenter.Agent"
cp -f "$tmp/update/servercenter-agent-update.sh" "$install_dir/servercenter-agent-update.sh"
chmod +x "$install_dir/servercenter-agent-update.sh"
printf '%s\n' "$remote" > "$version_file"

cp -f "$tmp/servercenter-agent.service" /etc/systemd/system/
systemctl daemon-reload
systemctl restart servercenter-agent
echo "agent updated to $remote"
