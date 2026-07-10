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

- Docker (or Podman) with `docker compose`.
- libvirt running; the RW socket at `/var/run/libvirt/libvirt-sock`; `virsh list --all` works as the
  user Docker runs as.
- At least one defined guest domain for the libvirt smoke (e.g. `virsh list --all` shows `plex-vm`).
- The controller listens h2c (HTTP/2 cleartext) in plaintext mode, so REST calls need
  `curl --http2-prior-knowledge` (or use the dashboard, which speaks gRPC/HTTP-2).

## 1. Bring up the controller  [works today]

```bash
cd Deploy/controller
mkdir -p templates/cs2                       # config templates, keyed by schemaRef
# put your server.cfg template at templates/cs2/server.cfg, e.g.:  hostname={{name}}\nrcon_password={{rcon.password}}
docker compose up --build -d
docker compose logs -f                       # watch for startup + agent connects
```

Verify: the container is up, `/data/servercenter.db` exists in the `controller-data` volume, no errors
in the logs. Note the host IP/hostname agents will dial (e.g. `10.0.0.2:5080`).

## 2. Install node zero (the host agent)  [works today]

On the hypervisor host itself (node zero runs the SAME agent as guests):

- Install from the GitHub release or `Scripts/publish-agent.sh linux-x64` + `Deploy/install.sh`.
- Configure `Deploy/servercenter-agent.env`: point `SERVERCENTER_CONTROLLER_ADDRESS` at
  `http://<host>:5080`, set `SERVERCENTER_NODE_KIND=host`, and the agent id.
- `systemctl start servercenter-agent` (Type=notify, journald).

Verify: controller logs `Agent <id> connected`; `journalctl -u servercenter-agent` shows the dial loop
settle.

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
docker compose exec controller sqlite3 /data/servercenter.db \
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
docker compose exec controller sh -c 'sqlite3 /data/servercenter.db "VACUUM INTO '\''/data/backup.db'\''"'
```

Copy `/data/backup.db` off-box. Test the restore into a scratch controller before trusting it.

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
