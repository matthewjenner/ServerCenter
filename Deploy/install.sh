#!/usr/bin/env bash
#
# Installs the ServerCenter agent as a systemd service on a Linux node (host or guest - the
# package is identical). Run as root from the extracted release directory.
#
# After install, edit /etc/servercenter-agent/agent.env (controller address + one-time enroll
# token; set SERVERCENTER_NODE_KIND=host on the hypervisor), then start the service.
#
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
    echo "error: run as root (sudo ./install.sh)" >&2
    exit 1
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

install_dir=/opt/servercenter-agent
state_dir=/var/lib/servercenter-agent
config_dir=/etc/servercenter-agent
service=servercenter-agent

# Dedicated unprivileged system user.
if ! id -u servercenter >/dev/null 2>&1; then
    useradd --system --home-dir "$state_dir" --shell /usr/sbin/nologin servercenter
fi

mkdir -p "$install_dir" "$state_dir/identity" "$config_dir"

# Binary (published self-contained; no .NET runtime required on the node).
cp -f "$script_dir/bin/"* "$install_dir/"
chmod +x "$install_dir/ServerCenter.Agent"

# Config: never clobber an existing edited env file.
if [[ ! -f "$config_dir/agent.env" ]]; then
    cp "$script_dir/servercenter-agent.env" "$config_dir/agent.env"
fi
chmod 600 "$config_dir/agent.env"

chown -R servercenter:servercenter "$install_dir" "$state_dir"

cp -f "$script_dir/servercenter-agent.service" /etc/systemd/system/
systemctl daemon-reload
systemctl enable "$service"

echo "Installed. Now:"
echo "  1. Edit $config_dir/agent.env  (controller address + one-time SERVERCENTER_ENROLL_TOKEN)."
echo "     On the hypervisor host, set SERVERCENTER_NODE_KIND=host."
echo "  2. systemctl start $service"
echo "  3. journalctl -u $service -f   # watch it enroll and connect"
