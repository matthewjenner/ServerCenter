#!/usr/bin/env bash
#
# Installs the ServerCenter agent as a systemd service on a Linux node (host or guest - the
# package is identical). Run as root from the extracted release directory.
#
#   sudo ./install.sh                    # install the agent; you edit agent.env + start it
#   sudo ./install.sh --with-controller  # ALSO stand up the controller container here (node zero),
#                                        # then wire this host agent to it and start - fully turnkey.
#
# --with-controller is for the hypervisor (node zero): if no controller is already running here it
# runs the bundled docker-compose.yml (pulls the published image - no source, nothing copied), then
# seeds this host's agent.env for the local plaintext controller and starts the service. If a
# controller is already running it is left untouched and only the agent is installed.
#
set -euo pipefail

with_controller=false
for arg in "$@"; do
    case "$arg" in
        --with-controller) with_controller=true ;;
        *) echo "usage: $0 [--with-controller]" >&2; exit 1 ;;
    esac
done

if [[ "${EUID}" -ne 0 ]]; then
    echo "error: run as root (sudo ./install.sh)" >&2
    exit 1
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

install_dir=/opt/servercenter-agent
state_dir=/var/lib/servercenter-agent
config_dir=/etc/servercenter-agent
controller_dir=/opt/servercenter-controller
service=servercenter-agent

controller_running() {
    docker ps --format '{{.Names}}' 2>/dev/null | grep -qx servercenter-controller
}

# --with-controller: bring up the controller container first, so the host agent has something to
# reach when it starts at the end of this script.
if [[ "$with_controller" == true ]]; then
    if ! command -v docker >/dev/null 2>&1; then
        echo "error: --with-controller needs Docker, which was not found on PATH." >&2
        exit 1
    fi
    if controller_running; then
        echo "==> Controller 'servercenter-controller' already running; leaving it as-is."
    else
        echo "==> Bringing up the controller container (bundled compose, pulls the published image)"
        mkdir -p "$controller_dir/templates"
        cp -f "$script_dir/controller/docker-compose.yml" "$controller_dir/docker-compose.yml"
        docker compose -f "$controller_dir/docker-compose.yml" pull
        docker compose -f "$controller_dir/docker-compose.yml" up -d
    fi
fi

# Dedicated unprivileged system user.
if ! id -u servercenter >/dev/null 2>&1; then
    useradd --system --home-dir "$state_dir" --shell /usr/sbin/nologin servercenter
fi

mkdir -p "$install_dir" "$state_dir/identity" "$config_dir"

# Binary (published self-contained; no .NET runtime required on the node).
cp -f "$script_dir/bin/"* "$install_dir/"
chmod +x "$install_dir/ServerCenter.Agent"

# Config: never clobber an existing edited env file.
fresh_env=false
if [[ ! -f "$config_dir/agent.env" ]]; then
    cp "$script_dir/servercenter-agent.env" "$config_dir/agent.env"
    fresh_env=true
fi
chmod 600 "$config_dir/agent.env"

# With --with-controller, this host IS node zero talking to its local plaintext controller, so seed
# the freshly-created env end to end (host kind, loopback controller, stable id) - no manual edit.
# An existing env is respected (never clobbered), matching the guest path.
seeded_host=false
if [[ "$with_controller" == true && "$fresh_env" == true ]]; then
    sed -i \
        -e 's|^SERVERCENTER_CONTROLLER=.*|SERVERCENTER_CONTROLLER=http://127.0.0.1:5080|' \
        -e 's|^SERVERCENTER_NODE_KIND=.*|SERVERCENTER_NODE_KIND=host|' \
        -e 's|^SERVERCENTER_AGENT_ID=.*|SERVERCENTER_AGENT_ID=host|' \
        "$config_dir/agent.env"
    seeded_host=true
fi

chown -R servercenter:servercenter "$install_dir" "$state_dir"

cp -f "$script_dir/servercenter-agent.service" /etc/systemd/system/
systemctl daemon-reload
systemctl enable "$service"

if [[ "$seeded_host" == true ]]; then
    systemctl restart "$service"
    echo "Installed node zero (host agent + controller)."
    echo "  Controller: http://127.0.0.1:5080 (plaintext bring-up)."
    echo "  journalctl -u $service -f   # watch it connect"
else
    echo "Installed. Now:"
    echo "  1. Edit $config_dir/agent.env  (SERVERCENTER_CONTROLLER + a UNIQUE SERVERCENTER_AGENT_ID"
    echo "     for plaintext http, or SERVERCENTER_ENROLL_TOKEN for mTLS https)."
    echo "  2. systemctl start $service"
    echo "  3. journalctl -u $service -f   # watch it connect"
fi
