# ServerCenter Agent - Linux install

The same agent runs on the hypervisor host (node zero) and on every managed guest. This
package is self-contained (no .NET runtime needed on the node).

## Install

1. Copy the release tarball to the node and extract it:
   ```
   tar -xzf servercenter-agent-<version>-linux-x64.tar.gz
   cd stage   # or wherever it extracted
   sudo ./install.sh
   ```
2. Edit `/etc/servercenter-agent/agent.env`:
   - `SERVERCENTER_CONTROLLER` - the controller address, e.g. `https://your-host:5443`.
   - `SERVERCENTER_ENROLL_TOKEN` - a one-time enrollment token minted by the controller.
   - `SERVERCENTER_NODE_KIND` - leave `guest`, or set `host` on the hypervisor (node zero).
3. Start and watch it enroll + connect:
   ```
   sudo systemctl start servercenter-agent
   journalctl -u servercenter-agent -f
   ```

## Node zero (the hypervisor host)

Node zero is not special software - it is this same agent, installed on the host, with
`SERVERCENTER_NODE_KIND=host`. Host management (updates, health, reboot-required) then flows
through the identical agent interfaces; the host's extra behavior (e.g. drain guests before a
reboot) is applied by the controller as policy, not by different agent code.

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
