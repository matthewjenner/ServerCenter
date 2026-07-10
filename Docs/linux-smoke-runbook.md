# Linux end-to-end smoke runbook

Bring the full Linux stack up on the real hypervisor + a guest and smoke each vertical against real
infra. This is the Tier-2/Tier-3 pass that the unit/integration suite cannot cover. Windows and S3
backup are deferred (see the workplan Decisions Log, 2026-07-10).

**First pass is PLAINTEXT (h2c) on a trusted LAN** - it proves the whole stack works today. mTLS is a
hardening follow-up (see Known gaps). ASCII punctuation only; scripts are bash from Git Bash.

Each step notes: the operator action, and what to verify. Steps are ordered; a later one assumes the
earlier ones passed. The `[works today]` / `[needs seeding]` / `[gap]` tags say how real the path is.

---

## 0. Prereqs (on the hypervisor host)

- Docker with `docker compose` (the image is public on GHCR - no login needed).
- libvirt running; the RW socket at `/var/run/libvirt/libvirt-sock`; `virsh list --all` works as the
  user Docker runs as. VMs under `qemu:///system` (the compose mounts the system socket).
- At least one defined guest domain for the libvirt smoke (e.g. `virsh list --all` shows `plex-vm`).
- The controller listens h2c (HTTP/2 cleartext) in plaintext mode, so REST calls need
  `curl --http2-prior-knowledge` (or use the dashboard, which speaks gRPC/HTTP-2).

## 1. Bring up node zero: controller + host agent, one command  [works today]

The hypervisor is node zero - it runs the same agent as guests. Because you install that agent
anyway, `install.sh --with-controller` also stands up the controller container, so node zero is a
single command (nothing copied from a workstation - it all comes from the GitHub release + the
public GHCR image):

```bash
curl -L -O https://github.com/matthewjenner/ServerCenter/releases/download/agent-v<version>/servercenter-agent-<version>-linux-x64.tar.gz
mkdir agent && tar -xzf servercenter-agent-<version>-linux-x64.tar.gz -C agent && cd agent
sudo ./install.sh --with-controller
```

This pulls + starts the controller on `http://127.0.0.1:5080` (plaintext bring-up), seeds the host
agent's env (`SERVERCENTER_NODE_KIND=host`, `SERVERCENTER_AGENT_ID=host`, loopback controller), and
starts the service. If a controller is already running here it is left as-is and only the agent
installs. Config templates go under `/opt/servercenter-controller/templates/<schemaRef>/` (mounted
read-only into the container at `/templates`), e.g. `/opt/servercenter-controller/templates/cs2/server.cfg`
with `hostname={{name}}` / `rcon_password={{rcon.password}}`.

Verify: `docker ps` shows `servercenter-controller` up; `/data/servercenter.db` exists in the
`controller-data` volume; `journalctl -u servercenter-agent -f` settles and the controller logs
`Agent host connected`. Note the host LAN IP guests will dial (e.g. `10.0.0.2:5080`).

Controller-only alternative (a box that is not itself a managed node): copy just
`Deploy/controller/docker-compose.yml` + a `templates/` dir and run `docker compose pull && up -d`.

## 2. Add the guest agents  [works today]

On each guest VM (same tarball; the VM keeps running - you are only adding a systemd service):

```bash
curl -L -O https://github.com/matthewjenner/ServerCenter/releases/download/agent-v<version>/servercenter-agent-<version>-linux-x64.tar.gz
mkdir agent && tar -xzf servercenter-agent-<version>-linux-x64.tar.gz -C agent && cd agent
sudo ./install.sh --controller <host-lan-ip>            # or --controller http://<host>:5080
```

The installer configures the env and starts the service. The node id defaults to the guest's
hostname (unique); pass `--agent-id <id>` to override. Run `sudo ./install.sh` with no `--controller`
and it prompts for the address. Verify: the controller logs `Agent <id> connected` and the node
appears in the dashboard. Then `journalctl -u servercenter-agent -f`.

## 3. Dashboard (dual-truth)  [works today]

Run the Avalonia UI (on your workstation) against the controller. Verify node zero appears with
**Agent = Online** and **VM = Unknown** (the host has no libvirt domain). This is the headline
dual-truth view.

## 4. Service control - Phase 3  [works today]

Dashboard: enter node zero's agent id + a real unit (e.g. `plexmediaserver.service`) and click
**Restart service**. Or:

```bash
curl --http2-prior-knowledge -sX POST http://<host>:5080/jobs/service-restart \
  -H 'content-type: application/json' -d '{"agentId":"<id>","unit":"plexmediaserver.service"}'
```

Verify: the jobs panel shows the job reach **Succeeded**; `systemctl status <unit>` shows a fresh
restart.

## 5. Updates - Phase 4  [works today]

Store a policy then trigger it (a manual trigger overrides the window and is its own confirmation):

```bash
curl --http2-prior-knowledge -sX POST http://<host>:5080/update-policies \
  -H 'content-type: application/json' \
  -d '{"id":"host-apt","version":1,"what":{"provider":"apt"},"how":"in-place","when":{"mode":"manual"},"reboot":"if-required","preflight":["notify"],"approval":"auto"}'

curl --http2-prior-knowledge -sX POST http://<host>:5080/jobs/update-apply \
  -H 'content-type: application/json' -d '{"agentId":"<id>","policyId":"host-apt"}'
```

Verify: the job runs `apt-get update` + `apt-get upgrade -y` on node zero and reaches Succeeded; a
note reports whether a reboot is required. For a Plex update, store a policy with
`"what":{"provider":"plex"}` and `"how":"stop-update-start"`, and dispatch with a `serviceUnit`.
Also confirm onboarding neuter: `systemctl is-enabled apt-daily.timer` is `masked`.

## 6. libvirt / VM lifecycle - Phase 6  [works today]

Link a guest node to its libvirt domain, then drive it. First, the guest's agent must have checked in
(so its node row exists), or provision it (step 8). Then:

```bash
curl --http2-prior-knowledge -sX POST http://<host>:5080/nodes/<nodeId>/libvirt-domain \
  -H 'content-type: application/json' -d '{"domain":"plex-vm"}'
```

Verify: the dashboard now shows that node's **VM** column as Running/Stopped (real libvirt truth, from
the state poller) independent of its Agent column. Then use the dashboard **VM controls** (node id +
Start/Stop/Restart) - each is a controller-driven job. Verify `virsh list --all` reflects the change
and the job shows Succeeded.

## 7. Game server - Phase 5  [needs seeding]

`server.install` (SteamCMD) and `server.config-apply` dispatch from a stored **game descriptor** + a
**server instance**. There is no HTTP endpoint to store those yet (see Known gaps), so seed them into
SQLite directly for now (the `body_json` must match `GameDescriptorSerializer`; keep it minimal):

```bash
docker exec servercenter-controller sqlite3 /data/servercenter.db \
  "INSERT INTO game_descriptor(id,version,body_json,created_at) VALUES
   ('cs2-dedicated',3,'{\"id\":\"cs2-dedicated\",\"version\":3,\"steamApp\":{\"appId\":730,\"installDir\":\"/opt/cs2\"}}',0);
   INSERT INTO server_instance(id,node_id,descriptor_id,descriptor_version,instance_params_json,created_at) VALUES
   ('srv-cs2','<guestNodeId>','cs2-dedicated',3,'{\"name\":\"ffa\",\"ports\":{\"game\":27015}}',0);"

curl --http2-prior-knowledge -sX POST http://<host>:5080/jobs/server-install \
  -H 'content-type: application/json' -d '{"agentId":"<guestAgentId>","instanceId":"srv-cs2"}'
```

Verify (on the guest): SteamCMD installs app 730 to `/opt/cs2`; the job reaches Succeeded. Then
`/jobs/server-config-apply` renders the config from the instance params (needs the descriptor to
declare `configGen` + a template on the controller under `templates/`).

## 8. Provisioning + recipes - Phase 7  [needs seeding]

Full build-from-a-recipe stands a server up from nothing:

- Seed a `build_recipe` (matching `BuildRecipeSerializer`) + a `server_instance` pinning
  `recipe_id`/`recipe_version` (same `sqlite3` approach as step 7).
- Dispatch: `POST /jobs/recipe-apply {agentId, instanceId}`.
- Verify (on the guest): base packages installed, SteamCMD app present, config written, scripts run,
  the systemd unit written + enabled + started; job Succeeded. **Re-run it** to prove convergence
  (already-done steps skip; build = repair = rebuild).

Provisioning handoff: `POST /nodes/provision {nodeId, kind, libvirtDomain}` records a node
`provisioning`; when its agent first checks in it flips to `managed` and keeps its domain. (The VM
define + cloud-init that actually boots it is Tier-3 - see gaps.)

## 9. Controller backup drill  [manual until ~1.0]

S3 backup is deferred. For now, snapshot the precious state with a consistent copy (never copy the
live file):

```bash
docker exec servercenter-controller sh -c 'sqlite3 /data/servercenter.db "VACUUM INTO '\''/data/backup.db'\''"'
```

Copy `/data/backup.db` off-box. Test the restore into a scratch controller before trusting it.

## 10. Auto-update (controller-distributed)  [works today]

Both tiers self-update on a timer. The controller is the only tier that reaches the internet (it
pulls its image from GHCR); agents pull their bundle FROM the controller, never GitHub.

- The controller advertises the target version + serves the bundles (baked into its image):
  ```bash
  curl --http2-prior-knowledge -s http://<host>:5080/agent/version                                  # {"version":"<x.y.z>"}
  curl --http2-prior-knowledge -s -o /tmp/a.tgz http://<host>:5080/agent/bundle/linux-x64 && ls -l /tmp/a.tgz
  ```
- `install.sh` set up the timers on every node (dev cadence ~5 min; eventual default daily):
  ```bash
  systemctl list-timers 'servercenter-*'
  ```
- Force a cycle now instead of waiting for the timer:
  ```bash
  sudo systemctl start servercenter-controller-update.service   # node zero: docker compose pull && up -d
  sudo systemctl start servercenter-agent-update.service        # any node: pull a newer bundle from the controller
  journalctl -u servercenter-agent-update.service --no-pager | tail
  ```

Verify: `cat /opt/servercenter-agent/VERSION` matches `/agent/version`. After you publish a newer
version, the controller's timer pulls the new image (with new baked bundles) and each agent's next
tick rolls forward (binary swapped + service restarted) - no per-node action.

---

## Updating an existing deployment (teardown + reinstall)

Once nodes carry the update timers, `0.1.x -> 0.1.y` is automatic (step 10). This section is only for
the ONE-TIME jump from a pre-auto-update build, or a clean reset - and only after the target
`agent-v<version>` + `controller-v<version>` releases exist (push -> CI green), or you will reinstall
the old version.

Controller (on the HV):
```bash
sudo docker compose -f /opt/servercenter-controller/docker-compose.yml down -v   # -v WIPES controller-data (SQLite)!
sudo systemctl disable --now servercenter-controller-update.timer 2>/dev/null || true
sudo rm -f /etc/systemd/system/servercenter-controller-update.service /etc/systemd/system/servercenter-controller-update.timer
sudo systemctl daemon-reload
sudo rm -rf /opt/servercenter-controller
sudo docker image rm ghcr.io/matthewjenner/servercenter-controller:latest 2>/dev/null || true
```
`down -v` deletes the `controller-data` volume (CA + node registry) - fine for a dev reset, but in
real operation that volume is your one-backup surface: keep it (`down` without `-v`) or back it up
first (step 9). Fallback if the compose file is already gone:
`sudo docker rm -f servercenter-controller && sudo docker volume rm servercenter-controller_controller-data`.

Agent (on the HV, and each guest):
```bash
sudo systemctl disable --now servercenter-agent.service
sudo systemctl disable --now servercenter-agent-update.timer 2>/dev/null || true
sudo rm -f /etc/systemd/system/servercenter-agent.service \
           /etc/systemd/system/servercenter-agent-update.service \
           /etc/systemd/system/servercenter-agent-update.timer
sudo systemctl daemon-reload
sudo rm -rf /opt/servercenter-agent /etc/servercenter-agent /var/lib/servercenter-agent
sudo userdel servercenter 2>/dev/null || true
```
The `2>/dev/null || true` bits keep it clean on a pre-auto-update node (no update timers to remove).
Then reinstall from the new release (steps 1-2).

---

## Known gaps / recommended follow-ups (surfaced by this runbook)

1. **No bootstrap-token mint endpoint** - so the mTLS `/enroll` flow has no operator step to create a
   token; the smoke uses plaintext h2c. FIX: a small `POST /admin/bootstrap-tokens` calling the trust
   provider's issue-token. Until then, mTLS on :5443 is not operable end to end.
2. **No store endpoints for descriptors / recipes / instances** - P5/P7 setup needs direct `sqlite3`
   seeding (steps 7-8). FIX: raw-body `POST /game-descriptors`, `/build-recipes`, `/server-instances`
   mirroring `POST /update-policies`. This is the biggest ergonomics gap for a real run.
3. **Server TLS cert subject is `localhost`** - fine today because the agent validates by CA chain,
   not hostname (no SAN check). If hostname pinning is ever added, make the subject configurable.
4. **VM bring-up is Tier-3 / not automated** - `ILibvirtHost` has no `Define`; cloud-init first boot +
   `virsh define` of a base image is done out of band. Step 8's handoff assumes the VM already boots.
5. **Controller runs as root + plaintext in this bring-up** - acceptable for a trusted-LAN first
   smoke; tighten (mTLS, least-privilege socket access) before it is anything but a homelab.
6. **Auto-update is plaintext-only + ungated** - the agent self-updater (`servercenter-agent-update
   .timer`) pulls bundles from the controller over http (h2c); the https/mTLS path needs the agent's
   client cert wired into the updater's curl. And it serves ANY caller - restricting `/agent/bundle`
   to APPROVED agents is a retrofit once the pending->approve trust model lands. No rollback yet
   (blue-green + watchdog is Phase 10).
