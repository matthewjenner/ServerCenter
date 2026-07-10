# Backup and Restore Runbook

Status: draft for review. ASCII punctuation only (house rule).

Governing invariant (brief 3.9): all precious state lives on the controller. The
controller's SQLite database, shipped to S3, is the single control-plane backup surface.
Agents and VMs are disposable. Data-plane bulk is backed up by purpose, not lumped in.

Public-repo note: this repo is public. No AWS account ids, bucket names, ARNs, keys, or
other doxxable/secret values go in code or docs. Bucket name and creds are runtime
configuration, injected at container start, never baked into the image or committed.

---

## 1. What is backed up, and where

| Class            | Contents                                                      | Path / mechanism |
| ---------------- | ------------------------------------------------------------ | ---------------- |
| Control plane    | identities, game descriptors, update policies, build recipes, instance params (incl. secrets), job history | SQLite -> `VACUUM INTO` snapshot -> versioned S3 bucket |
| Game save files  | per-game save/world file-sets                                | file-set backup primitive -> its OWN S3 path, per-game |
| Base OS image    | public hardened base image (rebuildable, NOT precious)       | not backed up as precious; rebuild from public source + hardening recipe |

There are no golden VM images (brief 3.10), so there is no fat artifact to store or cache.
Wanting to back up an agent is a smell: precious state leaked out of the controller; move
it back.

## 2. Controller DB backup (the precious surface)

Never copy the live SQLite file. WAL-mode writes in flight produce a torn, inconsistent
copy. Take a consistent snapshot instead.

Procedure (runs as a scheduled controller job, so it is visible in job history like any
mutation):

1. `VACUUM INTO '/tmp/servercenter-<ulid>.db'` (or the SQLite online-backup API). This
   yields a single consistent file with no WAL sidecar. Prefer `VACUUM INTO` for the
   compaction; use the online-backup API if a hot page-by-page copy is preferred under
   heavy write load.
2. Compute sha256 of the snapshot; record bytes.
3. Upload to the versioned S3 bucket under a time-ordered key. Capture the returned S3
   `versionId`.
4. Insert a `backup_snapshot` row (kind `controller-db`, s3_uri, s3_version_id, bytes,
   sha256, taken_at).
5. Delete the local temp snapshot.

S3 configuration:
- Bucket has versioning ENABLED (protects against overwrite/corruption/ransomware of the
  latest object).
- Lifecycle rule expires old noncurrent versions after a retention window (e.g. 30 days),
  so history is bounded and cost-controlled.
- IAM is scoped to exactly this one bucket, least-privilege (put/get/list/versioned-get on
  the one bucket, nothing else). Creds injected at runtime; never in the image or repo.

## 3. Game save backup (data-plane, separate)

Driven by the `saveBackup` capability on a game descriptor (section 4 of the contracts
doc), which selects the file-set backup primitive. Optional pre-backup quiesce via the
RCON primitive (flush/announce/pause) so the save is consistent. Writes to the game's own
S3 path, tracked as `backup_snapshot` kind `game-saves`. This is deliberately not lumped
with the control-plane DB: different cadence, different retention, different owner.

## 4. Restore: test it, do not just take it

A backup that has never been restored is a hypothesis. Restore is a first-class, tested
procedure.

### 4.1 Controller DB restore

1. Stop the controller (or bring up a restore instance pointed at a scratch DB path).
2. Fetch the chosen snapshot object version from S3 (by `s3_version_id`, not just latest).
3. Verify sha256 against the `backup_snapshot` row before trusting the file.
4. Place it as the controller DB, start in a read-only / verification mode first.
5. Verification checks (automated, part of the restore test):
   - `PRAGMA integrity_check;` returns `ok`.
   - Row counts for `agent_identity`, `node`, `job`, and the three class tables are
     non-zero and within expected bounds.
   - A known `server_instance` resolves its pinned descriptor/policy/recipe versions.
6. Bring the controller up. Agents re-establish their outbound streams and resync job
   state (contracts section 2.3). VM-running truth re-derives from the libvirt event
   stream. No agent-side precious state is needed for a full recovery.

### 4.2 Restore test cadence and record

- A scheduled restore test runs against a scratch instance (not production), on a cadence
  (e.g. weekly). On success it stamps `backup_snapshot.restore_tested_at`.
- The dashboard surfaces "latest snapshot" and "latest successfully restored snapshot"
  separately, so a silently-failing backup pipeline is visible.

### 4.3 Full rebuild-from-nothing drill (the real proof)

Exercises brief 3.10's promise ("rebuild a CS2 server from nothing"):
1. Restore the controller DB from S3 (4.1).
2. Provision a generic VM: public base image -> libvirt define/start -> cloud-init installs
   the agent -> agent phones home -> node is `managed` but empty.
3. Apply build recipe vN with the server's instance params -> running server.
4. Restore that server's save files from its S3 path (3).
Every input is a public image or controller-owned text plus the two S3 backup surfaces.

## 5. Host reboot interaction (node zero)

A host reboot takes down every guest and the controller container (brief 3.4). It does not
threaten precious state (already in S3), but the controller cannot observe its own return.
Operational expectation: the UI loses the controller, shows it, and reconnects when the
host is back; agents re-stream and resync. Do not attempt to have the controller narrate
its own host's reboot. A snapshot-first preflight on the host update policy is recommended
so a fresh controller-DB snapshot exists immediately before the risky operation.
