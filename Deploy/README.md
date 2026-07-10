# ServerCenter Agent - Linux install

The same agent runs on the hypervisor host (node zero) and on every managed guest. This
package is self-contained (no .NET runtime needed on the node).

## Install (a guest, or any node with a controller already running)

1. Download the release tarball onto the node and extract it (nothing is copied from a workstation
   - it all comes from the GitHub release):
   ```
   curl -L -O https://github.com/matthewjenner/ServerCenter/releases/download/agent-v<version>/servercenter-agent-<version>-linux-x64.tar.gz
   mkdir agent && tar -xzf servercenter-agent-<version>-linux-x64.tar.gz -C agent && cd agent
   sudo ./install.sh
   ```
2. Edit `/etc/servercenter-agent/agent.env`:
   - `SERVERCENTER_CONTROLLER` - the controller address (`http://<controller-host>:5080` for the
     plaintext bring-up, or `https://<controller-host>:5443` for mTLS).
   - Plaintext: `SERVERCENTER_AGENT_ID` - a UNIQUE id per node (blank collides on `dev-agent`).
   - mTLS: `SERVERCENTER_ENROLL_TOKEN` - a one-time token minted by the controller.
   - `SERVERCENTER_NODE_KIND` - leave `guest`.
3. Start and watch it connect:
   ```
   sudo systemctl start servercenter-agent
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

## Updating

Replace the binary and restart:
```
sudo systemctl stop servercenter-agent
sudo cp bin/* /opt/servercenter-agent/
sudo systemctl start servercenter-agent
```
The persisted identity under `/var/lib/servercenter-agent` is kept across updates. (Automated
self-update with a watchdog lands in a later phase.)

## Uninstall

```
sudo systemctl disable --now servercenter-agent
sudo rm -rf /opt/servercenter-agent /etc/servercenter-agent /var/lib/servercenter-agent
sudo rm /etc/systemd/system/servercenter-agent.service && sudo systemctl daemon-reload
sudo userdel servercenter
```
