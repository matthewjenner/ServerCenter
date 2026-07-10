#!/usr/bin/env bash
#
# Installs the ServerCenter agent as a systemd service on a Linux node (host or guest - the
# package is identical). Run as root from the extracted release directory.
#
#   sudo ./install.sh --with-controller           # node zero: also bring up the controller here,
#                                                 # wire this host agent to it, and start.
#   sudo ./install.sh --controller 10.0.0.2       # a guest: point the agent at the controller.
#   sudo ./install.sh                             # a guest: prompts for the controller address.
#
set -euo pipefail

with_controller=false
controller=""
agent_id=""
node_kind=""

usage() {
    cat <<'EOF'
Usage: sudo ./install.sh [--with-controller | --controller <host-or-url>] [--agent-id <id>] [--node-kind <kind>]

  --with-controller        Node zero: also bring up the controller container here and wire this
                           host agent to http://127.0.0.1:5080. Mutually exclusive with --controller.
  --controller <addr>      Guest: the controller address. Accepts a full URL, host:port, or a bare
                           host/IP (assumed http://<host>:5080).
  --agent-id <id>          This node's id (default: the machine hostname; must be UNIQUE per node on
                           the plaintext/http path). Node zero defaults to "host".
  --node-kind <host|guest> Reported node kind (default guest; forced host with --with-controller).
  -h, --help               Show this help.

With neither --with-controller nor --controller, you are prompted for the controller address.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --with-controller) with_controller=true; shift ;;
        --controller) [[ $# -ge 2 ]] || { echo "error: --controller needs a value" >&2; exit 1; }; controller="$2"; shift 2 ;;
        --agent-id)   [[ $# -ge 2 ]] || { echo "error: --agent-id needs a value" >&2; exit 1; };   agent_id="$2";   shift 2 ;;
        --node-kind)  [[ $# -ge 2 ]] || { echo "error: --node-kind needs a value" >&2; exit 1; };  node_kind="$2";  shift 2 ;;
        -h|--help) usage; exit 0 ;;
        *) echo "error: unknown argument '$1' (see --help)" >&2; exit 1 ;;
    esac
done

if [[ "${EUID}" -ne 0 ]]; then
    echo "error: run as root (sudo ./install.sh)" >&2
    exit 1
fi

if [[ "$with_controller" == true && -n "$controller" ]]; then
    echo "error: --with-controller and --controller are mutually exclusive (--with-controller is local)." >&2
    exit 1
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

install_dir=/opt/servercenter-agent
state_dir=/var/lib/servercenter-agent
config_dir=/etc/servercenter-agent
controller_dir=/opt/servercenter-controller
service=servercenter-agent

# A bare host/IP becomes http://<host>:5080; host:port gets http://; a full URL passes through.
normalize_controller() {
    case "$1" in
        http://*|https://*) printf '%s' "$1" ;;
        *:*)                printf 'http://%s' "$1" ;;
        *)                  printf 'http://%s:5080' "$1" ;;
    esac
}

# Update KEY=... in an env file in place, or append it if absent (& \ | escaped for sed).
set_env_key() {
    local file="$1" key="$2" value="$3" esc
    if grep -qE "^${key}=" "$file"; then
        esc=$(printf '%s' "$value" | sed 's/[&\\|]/\\&/g')
        sed -i "s|^${key}=.*|${key}=${esc}|" "$file"
    else
        printf '%s=%s\n' "$key" "$value" >> "$file"
    fi
}

# Resolve this node's connection config from the flags (prompting a guest for the controller).
if [[ "$with_controller" == true ]]; then
    controller_address="http://127.0.0.1:5080"
    node_kind="${node_kind:-host}"
    agent_id="${agent_id:-host}"
else
    if [[ -z "$controller" && -t 0 ]]; then
        read -r -p "Controller address (host IP or URL, e.g. 10.0.0.2): " controller
    fi
    if [[ -z "$controller" ]]; then
        echo "error: no controller address. Pass --controller <host-or-url>, or --with-controller for node zero." >&2
        exit 1
    fi
    controller_address="$(normalize_controller "$controller")"
    node_kind="${node_kind:-guest}"
    agent_id="${agent_id:-$(hostname)}"
fi

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

# Version marker + the self-updater (driven by servercenter-agent-update.timer). The updater compares
# this VERSION against what the controller serves and pulls a newer bundle from the controller.
cp -f "$script_dir/VERSION" "$install_dir/VERSION" 2>/dev/null || printf 'unknown\n' > "$install_dir/VERSION"
cp -f "$script_dir/update/servercenter-agent-update.sh" "$install_dir/servercenter-agent-update.sh"
chmod +x "$install_dir/servercenter-agent-update.sh"

# Config: create from the template if absent, then apply the resolved connection values. Other keys
# (cert dir, and any mTLS enroll token) are left untouched.
if [[ ! -f "$config_dir/agent.env" ]]; then
    cp "$script_dir/servercenter-agent.env" "$config_dir/agent.env"
fi
set_env_key "$config_dir/agent.env" SERVERCENTER_CONTROLLER "$controller_address"
set_env_key "$config_dir/agent.env" SERVERCENTER_NODE_KIND "$node_kind"
set_env_key "$config_dir/agent.env" SERVERCENTER_AGENT_ID "$agent_id"
chmod 600 "$config_dir/agent.env"

chown -R servercenter:servercenter "$install_dir" "$state_dir"

cp -f "$script_dir/servercenter-agent.service" /etc/systemd/system/

# Self-update units: the agent periodically pulls a newer bundle from the controller.
cp -f "$script_dir/update/servercenter-agent-update.service" \
      "$script_dir/update/servercenter-agent-update.timer" /etc/systemd/system/

# Node zero also auto-updates the controller container image (pull + recreate).
if [[ "$with_controller" == true ]]; then
    cp -f "$script_dir/controller/servercenter-controller-update.service" \
          "$script_dir/controller/servercenter-controller-update.timer" /etc/systemd/system/
fi

systemctl daemon-reload
systemctl enable "$service"
systemctl restart "$service"
systemctl enable --now servercenter-agent-update.timer
if [[ "$with_controller" == true ]]; then
    systemctl enable --now servercenter-controller-update.timer
fi

echo "Installed."
if [[ "$with_controller" == true ]]; then
    echo "  Controller: http://127.0.0.1:5080 (plaintext bring-up), running here (auto-updates daily)."
fi
echo "  Agent id:   $agent_id  (kind $node_kind)  ->  $controller_address"
echo "  Auto-update: servercenter-agent-update.timer (pulls newer bundles from the controller)."
echo "  journalctl -u $service -f   # watch it connect"
