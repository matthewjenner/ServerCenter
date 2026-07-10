# ServerCenter Agent - Linux install

The same agent runs on the hypervisor host (node zero) and on every managed guest. This
package is self-contained (no .NET runtime needed on the node).

## Install (a guest, or any node with a controller already running)

Download the release tarball onto the node, extract it, and run the installer with the controller
address - it configures and starts the service for you (nothing copied from a workstation, no env
hand-editing):

```
curl -L -O https://github.com/matthewjenner/ServerCenter/releases/download/agent-v<version>/servercenter-agent-<version>-linux-x64.tar.gz
mkdir agent && tar -xzf servercenter-agent-<version>-linux-x64.tar.gz -C agent && cd agent
sudo ./install.sh --controller 10.0.0.2          # bare host/IP -> http://10.0.0.2:5080
```

`--controller` accepts a bare host/IP (assumed `http://<host>:5080`), a `host:port`, or a full URL.
Run `sudo ./install.sh` with no address and it prompts for one. The node id defaults to the machine
hostname (unique per node); override with `--agent-id <id>`. For mTLS instead of the plaintext
bring-up, pass `--controller https://<host>:5443` and set a one-time `SERVERCENTER_ENROLL_TOKEN` in
`/etc/servercenter-agent/agent.env` before it starts.

Watch it connect:
```
journalctl -u servercenter-agent -f
```

## Node zero (the hypervisor host) - one command

Node zero is not special software - it is this same agent on the host with
`SERVERCENTER_NODE_KIND=host`. Because you install the host agent anyway, `--with-controller` also
stands up the controller container from the bundled compose (pulling the published image), then
wires this host's agent to it and starts - no files copied, no manual env edit:

```
curl -L -O https://github.com/matthewjenner/ServerCenter/releases/download/agent-v<version>/servercenter-agent-<version>-linux-x64.tar.gz
mkdir agent && tar -xzf servercenter-agent-<version>-linux-x64.tar.gz -C agent && cd agent
sudo ./install.sh --with-controller
```

This brings up the controller on `http://127.0.0.1:5080` (plaintext bring-up), writes the host
agent's env (`SERVERCENTER_NODE_KIND=host`, `SERVERCENTER_AGENT_ID=host`, loopback controller), and
starts the service. If a controller is already running here it is left untouched and only the agent
is installed. Requires Docker on the host; the controller image is public on GHCR.

Host management (updates, health, reboot-required) then flows through the identical agent
interfaces; the host's extra behavior (e.g. drain guests before a reboot) is applied by the
controller as policy, not by different agent code.

## Updating (automatic)

`install.sh` sets up **automatic updates**, so you normally never touch a node again:

- **Agents** run `servercenter-agent-update.timer` (daily). It asks the controller what agent version
  it serves and, if newer, pulls that bundle **from the controller** (never GitHub - the controller is
  the distribution root), swaps the binary, and restarts. The persisted identity under
  `/var/lib/servercenter-agent` is kept. No rollback yet (blue-green + watchdog is Phase 10).
- **The controller** (node zero) runs `servercenter-controller-update.timer` (daily): `docker compose
  pull && up -d`. State lives in the `controller-data` volume, so a recreate is safe.

Because all three tiers share one version, bumping the controller to a new image automatically rolls
the agents forward on their next timer tick (the new agent bundles are baked into the controller
image). To force an update now:
```
sudo systemctl start servercenter-agent-update.service        # a node
sudo systemctl start servercenter-controller-update.service   # node zero
```

Manual fallback (replace the binary yourself):
```
sudo systemctl stop servercenter-agent && sudo cp bin/* /opt/servercenter-agent/ && sudo systemctl start servercenter-agent
```

## Uninstall

```
sudo systemctl disable --now servercenter-agent
sudo rm -rf /opt/servercenter-agent /etc/servercenter-agent /var/lib/servercenter-agent
sudo rm /etc/systemd/system/servercenter-agent.service && sudo systemctl daemon-reload
sudo userdel servercenter
```
