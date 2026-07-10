# Phase 0 Contracts and Foundations

Status: draft for review. Nothing downstream should be coded until this is agreed.
ASCII punctuation only (house rule). Testing, structure, and style follow the house
rules; this doc defines the contracts those rules will be applied to.

Contents:
1. Versioned protobuf envelope and the bidi stream
2. Job state machine, resync handshake, and SQLite schema
3. Agent platform interfaces
4. Capability contract (`ICapability`) and the game descriptor schema
5. UpdatePolicy schema
6. BuildRecipe schema
7. The shared primitive library
8. Identity and auth (controller-owned, with the seam)

---

## 1. Versioned protobuf envelope and the bidi stream

Contract-first `.proto` in `Contracts/`. We own the wire, so the envelope is versioned
from message one, tolerates unknown fields (proto3 default), and rejects cleanly on a
major-version mismatch.

### 1.1 Versioning rules

- `protocol_major` bump = breaking change. On mismatch, the receiver rejects the stream
  with a typed close (see `Hello` handshake) and does not attempt to interpret payloads.
- `protocol_minor` bump = additive only (new optional fields, new oneof arms). A newer
  peer talking to an older peer degrades gracefully: unknown fields are preserved and
  ignored; unknown oneof arms are treated as "no-op I do not understand" and logged.
- Never renumber or reuse a field tag. Reserve removed tags.

### 1.2 The stream

One long-lived bidi RPC. The agent dials out and opens it on startup; it is the only
agent-initiated connection.

```proto
syntax = "proto3";
package servercenter.v1;

service AgentLink {
  // Agent dials this. Bidi for the lifetime of the agent process / connection.
  rpc Connect(stream AgentMessage) returns (stream ControllerMessage);
}

message Envelope {
  uint32 protocol_major = 1;   // reject on mismatch
  uint32 protocol_minor = 2;   // additive
  string message_id     = 3;   // ulid, unique per message, for dedupe
  string correlation_id = 4;   // ties a reply/progress back to a request; empty if none
  int64  sent_unix_ms   = 5;   // sender clock, advisory only
}

message AgentMessage {
  Envelope envelope = 1;
  oneof payload {
    Hello           hello            = 10;  // first message after connect
    Heartbeat       heartbeat        = 11;
    NodeStatus      status           = 12;
    JobProgress     job_progress     = 13;
    JobResyncReport job_resync       = 14;  // sent in response to ResyncRequest
    CommandResult   command_result   = 15;
  }
}

message ControllerMessage {
  Envelope envelope = 1;
  oneof payload {
    HelloAck        hello_ack        = 10;  // includes negotiated protocol + session
    ResyncRequest   resync_request   = 11;  // controller asks "what jobs do you have?"
    Command         command          = 12;  // includes job-bearing commands
    CancelJob       cancel_job       = 13;
    Goodbye         goodbye          = 14;  // typed close: reason, e.g. VERSION_MISMATCH
  }
}
```

`Hello` carries the agent's pinned identity id, agent binary version, OS/arch, and its
current in-flight job ids (a cheap first hint before the full resync report). `HelloAck`
carries the negotiated `protocol_minor`, a server-assigned session id, and whether the
controller wants a full `ResyncRequest`.

### 1.3 Heartbeat and liveness

Heartbeat is an explicit message on an interval (default 10s), separate from HTTP/2 PING.
Missing N heartbeats moves agent-online to `Stale`, then to `Offline`. The stream
dropping is immediate `Offline`. Liveness is stream-derived, never UI-poll-derived.

### 1.4 The transport is injectable (testability constraint, decided now)

The `Connect` bidi stream sits behind a transport abstraction from message one, not the raw
gRPC channel inline. Production wires the real gRPC channel; tests inject a controllable
transport that can drop, delay, reorder, and partition messages. Resync correctness
(section 2.3) is only trustworthy if the disconnect paths are exercisable deterministically
at Tier 1 and via real chaos tooling at Tier 2 (see `testing.md`). This is painful to
retrofit, so the abstraction ships in Phase 1.

```csharp
// Both directions of the bidi stream, behind a seam. Production = gRPC; tests = in-memory.
public interface IAgentTransport {
    IAsyncEnumerable<ControllerMessage> Incoming(CancellationToken ct);
    ValueTask SendAsync(AgentMessage msg, CancellationToken ct);
}
// The chaos wrapper (drop/delay/partition) decorates any IAgentTransport in tests only.
```

---

## 2. Job model: state machine, resync, persistence

Every mutation is a job. The job model is built in Phase 1, including the resync
scaffolding, before any real job type exists. Retrofitting resync later is the exact
refactor we are avoiding.

### 2.1 State machine

```
                 +-------------------- cancel (if cancellable) ------+
                 v                                                   |
   [queued] --> [running] --> [succeeded]                           |
      |            |    \----> [failed]                              |
      |            |     \---> [timedout]                            |
      |            +---------> [cancelled] <--------------------------+
      +----------------------> [cancelled]   (cancel while queued: always allowed)

  Terminal states: succeeded | failed | timedout | cancelled.
  terminal_at is set exactly once, on entry to any terminal state.
```

- Cancellability is per job type, declared on the job type, not global. Mid-transaction
  `apt` is not cancellable; a stuck SteamCMD download is. A `CancelJob` for a
  non-cancellable running job is rejected with a typed result, not silently dropped.
- Timeout is per job type. On timeout the controller marks `timedout`; the agent, on next
  contact, reconciles (the work may have finished during the gap).

### 2.2 Job ownership and the disconnect contract

- The controller is the system of record for jobs. It assigns the job id (ulid), persists
  the job at `queued`, and only then sends the `Command` down.
- The agent keeps local, transient job state keyed by job id, for exactly as long as the
  job is non-terminal plus a short retention window after terminal (so a reconnect can
  report "it finished while you were gone").
- Jobs outlive the connection. When the stream drops, running work continues on the agent.
  Progress and log lines are buffered locally by the agent.

### 2.3 Resync handshake (built in Phase 1, before real jobs)

On every (re)connect, after `Hello` / `HelloAck`:

1. Controller sends `ResyncRequest` listing the job ids it believes are non-terminal for
   this agent (from SQLite).
2. Agent replies with `JobResyncReport`: for each job id it knows about, its local state
   (`still_running`, `finished_succeeded`, `finished_failed`, `unknown`), the last
   progress checkpoint, and buffered log lines since the last acknowledged sequence
   number.
3. Reconciliation rules:
   - Controller has `running`, agent says `still_running`: resume streaming; agent
     replays buffered progress/log from `last_acked_seq`.
   - Controller has `running`, agent says `finished_*`: controller writes the terminal
     state and the buffered tail, then closes the job.
   - Controller has `running`, agent says `unknown` (agent lost its state, e.g. rebuilt):
     controller marks the job `failed` with reason `lost_after_disconnect` unless the job
     type declares itself idempotently-requeueable, in which case it re-issues.
   - Agent reports a job the controller has no record of (should not happen; controller is
     the id source): controller logs and instructs the agent to drop it.
4. Log lines carry a monotonic per-job `seq`; the controller acks up to a seq so the agent
   can free its buffer. This bounds agent memory and makes replay exact.

### 2.4 SQLite schema (controller, the one precious surface)

```sql
-- Identity / trust. Controller owns this (see section 8).
CREATE TABLE agent_identity (
  id             TEXT PRIMARY KEY,      -- ulid, the pinned agent identity
  display_name   TEXT NOT NULL,
  cert_pem       TEXT NOT NULL,         -- pinned client cert (or its fingerprint)
  cert_fpr       TEXT NOT NULL UNIQUE,  -- sha256 fingerprint, used for pinning
  status         TEXT NOT NULL,         -- pending|active|revoked
  enrolled_at    INTEGER NOT NULL,      -- unix ms
  rotated_at     INTEGER,
  revoked_at     INTEGER
);

-- A managed node (guest or host). Node zero (the host) is a normal row here.
CREATE TABLE node (
  id             TEXT PRIMARY KEY,      -- ulid
  agent_id       TEXT REFERENCES agent_identity(id),
  kind           TEXT NOT NULL,         -- guest|host
  hostname       TEXT,
  os_family      TEXT,                  -- linux|windows
  lifecycle      TEXT NOT NULL,         -- provisioning|managed|decommissioned
  libvirt_domain TEXT,                  -- domain name/uuid if a guest; null for host
  created_at     INTEGER NOT NULL
);

-- The spine.
CREATE TABLE job (
  id             TEXT PRIMARY KEY,      -- ulid; controller-assigned
  node_id        TEXT NOT NULL REFERENCES node(id),
  type           TEXT NOT NULL,         -- e.g. service.restart, update.apply, recipe.apply
  params_json    TEXT NOT NULL,         -- typed per job type; validated on enqueue
  state          TEXT NOT NULL,         -- queued|running|succeeded|failed|timedout|cancelled
  progress_pct   INTEGER,               -- 0..100, nullable for indeterminate
  progress_note  TEXT,
  cancellable    INTEGER NOT NULL,      -- 0/1, copied from job-type at enqueue
  requeueable    INTEGER NOT NULL,      -- 0/1, drives resync 'unknown' handling
  last_acked_seq INTEGER NOT NULL DEFAULT 0,
  created_at     INTEGER NOT NULL,
  started_at     INTEGER,
  terminal_at    INTEGER,               -- set once, on terminal entry
  fail_reason    TEXT,
  correlation_id TEXT                   -- e.g. the parent recipe job, or UI request
);
CREATE INDEX ix_job_node_state ON job(node_id, state);
CREATE INDEX ix_job_state_open ON job(state) WHERE terminal_at IS NULL;

CREATE TABLE job_log (
  job_id         TEXT NOT NULL REFERENCES job(id),
  seq            INTEGER NOT NULL,      -- monotonic per job; ack watermark lives on job
  ts_unix_ms     INTEGER NOT NULL,
  stream         TEXT NOT NULL,         -- stdout|stderr|note
  line           TEXT NOT NULL,
  PRIMARY KEY (job_id, seq)
);

-- The three declarative surfaces: versioned CLASSES. Instance params are separate.
CREATE TABLE game_descriptor (
  id TEXT NOT NULL, version INTEGER NOT NULL, body_json TEXT NOT NULL,
  created_at INTEGER NOT NULL, PRIMARY KEY (id, version)
);
CREATE TABLE update_policy (
  id TEXT NOT NULL, version INTEGER NOT NULL, body_json TEXT NOT NULL,
  created_at INTEGER NOT NULL, PRIMARY KEY (id, version)
);
CREATE TABLE build_recipe (
  id TEXT NOT NULL, version INTEGER NOT NULL, body_json TEXT NOT NULL,
  created_at INTEGER NOT NULL, PRIMARY KEY (id, version)
);

-- Instance params: the per-server data that specializes a class. One class -> many.
CREATE TABLE server_instance (
  id TEXT PRIMARY KEY,                  -- ulid, a concrete running server
  node_id TEXT NOT NULL REFERENCES node(id),
  recipe_id TEXT, recipe_version INTEGER,
  descriptor_id TEXT, descriptor_version INTEGER,
  policy_id TEXT, policy_version INTEGER,
  instance_params_json TEXT NOT NULL,   -- hostname, ports, rcon pw (see section 8), slots
  created_at INTEGER NOT NULL
);

-- Backup bookkeeping (see backup-restore-runbook.md).
CREATE TABLE backup_snapshot (
  id TEXT PRIMARY KEY, kind TEXT NOT NULL,   -- controller-db|game-saves
  s3_uri TEXT NOT NULL, s3_version_id TEXT,
  bytes INTEGER, sha256 TEXT,
  taken_at INTEGER NOT NULL, restore_tested_at INTEGER
);
```

Notes:
- The three class tables store the schema body as validated JSON keyed by (id, version).
  Schemas are versioned data; a running `server_instance` pins the exact versions it was
  built or is governed by, so history reconstructs exactly (brief: "what happened last
  Tuesday").
- Job params and the three schema bodies are validated against typed C# models on write.
  JSON-in-SQLite keeps the schema stable while the class shapes evolve; the source of
  truth for shape is the C# record + a schema version, not the column layout.
- WAL mode on. Never copy the live file for backup; use `VACUUM INTO` (runbook).

---

## 3. Agent platform interfaces

.NET 10 runs both OSes, but the actions diverge hard. Agent commands go through these
interfaces; Ubuntu implementations first, Windows later against the same seams. All are
async and cancellation-aware.

Testability constraint (decided now): every one of these interfaces ships WITH a maintained
in-memory fake as part of its definition of done. The fakes let Tier 1 drive the full
system with zero real infra (see `testing.md`). This applies to every external-effect seam
in this doc - the four below, the capability contracts (section 4), the seam-primitives
(RCON client, file-set/S3 client, package/"what" providers, section 7), `ILibvirtHost`
(Phase 6), and `IAgentTrustProvider` (section 8). "No fake" means "not done."

```csharp
// Structured service state; prefer DBus/busctl on Linux and System.ServiceProcess on
// Windows over parsing `systemctl status` text.
public interface IServiceController {
    Task<ServiceState> GetStateAsync(string unit, CancellationToken ct);
    Task StartAsync(string unit, CancellationToken ct);
    Task StopAsync(string unit, CancellationToken ct);
    Task RestartAsync(string unit, CancellationToken ct);
    Task EnsureEnabledAsync(string unit, bool enabled, CancellationToken ct);
    IAsyncEnumerable<ServiceState> WatchAsync(string unit, CancellationToken ct); // change sub
}

// Pluggable "what" provider. apt AND an app channel (Plex) both implement this, so the
// abstraction is proven non-apt from day one and cannot ossify as "apt with extra steps".
public interface IUpdateProvider {
    string Channel { get; }                          // "apt" | "plex" | "wu" | ...
    Task<IReadOnlyList<AvailableUpdate>> CheckAsync(CancellationToken ct);
    Task<UpdateOutcome> ApplyAsync(UpdatePlan plan, IJobSink sink, CancellationToken ct);
    Task<bool> RebootRequiredAsync(CancellationToken ct);
}

public interface IProcessInspector {
    Task<IReadOnlyList<ProcessInfo>> ListAsync(ProcessQuery q, CancellationToken ct);
    Task<ProcessInfo?> FindAsync(ProcessQuery q, CancellationToken ct);
}

public interface ISystemInfo {
    Task<SystemFacts> GetFactsAsync(CancellationToken ct);   // os, arch, kernel, uptime
    Task<ResourceSample> SampleAsync(CancellationToken ct);  // cpu/mem/disk snapshot
    Task<bool> RebootPendingAsync(CancellationToken ct);
}

// How any long-running operation reports into the job spine. Progress/log flow up as
// JobProgress; the sink handles seq numbering and local buffering for resync.
public interface IJobSink {
    void Progress(int? pct, string? note);
    void Log(LogStream stream, string line);
}
```

`ServiceState` includes at least: `Active`, `Inactive`, `Failed`, `Activating`,
`Deactivating`, `Unknown`. Readiness is a separate concept (section 7): a service being
`Active` is not the same as a game "accepting players."

---

## 4. Capability contract and the game descriptor

A game capability is descriptor-selected behavior for config-gen, save-backup, stats, and
graceful shutdown. Both primitives and the plugin escape hatch implement the SAME
contract, so callers cannot tell primitive-backed from plugin-backed apart.

```csharp
public interface ICapability {
    CapabilityKind Kind { get; }   // ConfigGen | SaveBackup | Stats | Shutdown | Readiness
}

public interface IConfigGenCapability : ICapability {
    // schema -> render -> write to path P in format F. Backed by the config-templating
    // primitive for the common case, or a plugin for a bespoke format.
    Task ApplyAsync(ConfigContext ctx, IJobSink sink, CancellationToken ct);
}
public interface ISaveBackupCapability : ICapability {
    Task BackupAsync(SaveBackupContext ctx, IJobSink sink, CancellationToken ct);
    Task RestoreAsync(SaveRestoreContext ctx, IJobSink sink, CancellationToken ct);
}
public interface IStatsCapability : ICapability {
    Task<ServerStats> ReadAsync(StatsContext ctx, CancellationToken ct);   // often via RCON
}
public interface IShutdownCapability : ICapability {
    Task GracefulShutdownAsync(ShutdownContext ctx, IJobSink sink, CancellationToken ct);
}
public interface IReadinessCapability : ICapability {
    // readiness != process-alive; game-level "accepting players"
    Task<Readiness> ProbeAsync(ReadinessContext ctx, CancellationToken ct);
}
```

Game descriptor schema (stored as versioned JSON, `game_descriptor` table):

```jsonc
{
  "id": "cs2-dedicated",
  "version": 3,
  "steamApp": { "appId": 730, "installDir": "/opt/cs2", "betaBranch": null },
  "capabilities": {
    "configGen": { "primitive": "config-template",
                   "files": [ { "schemaRef": "cs2/server.cfg", "path": "/opt/cs2/cfg/server.cfg", "format": "kv" } ] },
    "saveBackup": { "primitive": "file-set",
                    "paths": [ "/opt/cs2/csgo/cfg", "/opt/cs2/maps/workshop" ],
                    "exclude": [ "*.tmp" ], "quiesce": { "via": "rcon", "command": "sv_shutdown_notice" } },
    "stats": { "primitive": "rcon", "commands": { "players": "status" } },
    "shutdown": { "primitive": "rcon", "drainCommand": "say Restarting; sv_shutdown", "graceSeconds": 60 },
    "readiness": { "primitive": "query-protocol", "protocol": "a2s", "port": "{{ports.game}}" }
  }
}
```

`{{...}}` placeholders bind to `server_instance.instance_params_json` at run time (class
vs instance split). A plugin-backed capability replaces `"primitive": "..."` with
`"plugin": "..."` and the rest of the system is unaffected.

---

## 5. UpdatePolicy schema

Pure data over the update primitives. Every example from the brief must express as data,
including the host's special reboot preflight.

```jsonc
{
  "id": "plex-lowtraffic",
  "version": 2,
  "what":      { "provider": "plex" },                 // apt | plex | apt-set | wu
  "how":       "stop-update-start",                    // in-place | stop-update-start | drain-then-update
  "when":      { "mode": "window", "cron": "0 4 * * *", "windowMinutes": 120 },
  "reboot":    "if-required",                          // never | if-required | always-after | prompt
  "preflight": [ "notify" ],                           // notify | snapshot-first | drain-players-via-rcon | quiesce
  "approval":  "auto"                                  // auto | require-confirmation
}
```

Host policy (node zero, brief 3.4) is the same shape with teeth:

```jsonc
{
  "id": "host-node-zero", "version": 1,
  "what": { "provider": "apt" }, "how": "in-place",
  "when": { "mode": "manual" },
  "reboot": "prompt",
  "preflight": [ "drain-players-via-rcon", "notify" ],  // warn/drain ALL guests first
  "approval": "require-confirmation"
}
```

Each policy execution is a job (section 2). `drain-players-via-rcon` is the same RCON
primitive as everything else (section 7). The host reboot job additionally triggers the
UI's expect-to-lose-controller-and-reconnect path; the controller cannot narrate its own
host's reboot.

---

## 6. BuildRecipe schema

Almost every field composes a primitive that already exists for another reason, so the
recipe engine lands late and cheap (Phase 7). Every step is idempotent / convergent.

```jsonc
{
  "id": "cs2-server", "version": 5,
  "baseRequirements": { "provider": "apt", "packages": [ "lib32gcc-s1", "steamcmd" ] },
  "steamApp": { "appId": 730, "installDir": "/opt/cs2", "validate": true },
  "configFiles": [
    { "schemaRef": "cs2/server.cfg", "path": "/opt/cs2/cfg/server.cfg", "format": "kv" }
  ],
  "scripts": [
    { "id": "workshop-collection", "run": "install-collection.sh",
      "alreadyDone": "test -f /opt/cs2/.collection-ok", "onSuccess": "touch /opt/cs2/.collection-ok" }
  ],
  "serviceDefinition": { "unit": "cs2.service", "execStart": "/opt/cs2/start.sh",
                         "user": "cs2", "restart": "on-failure" },
  "descriptorRef": { "id": "cs2-dedicated", "version": 3 }
}
```

Convergence rule (brief 3.12): every step carries an "already done?" check and biases to
`ensure-*` semantics (`ensure-installed`, `ensure-file-matches`, `ensure-service-enabled`)
over imperative (`install`, `write`, `start`). Re-applying a recipe to a half-built or
drifted box converges rather than double-installing or failing. Build = repair = rebuild
is then one operation.

---

## 7. The shared primitive library

Write once, reuse across all three declarative surfaces. This is where the "less total
code" pressure is spent: named primitives absorb would-be special cases.

| Primitive              | Contract sketch                                             | Absorbs (special cases it prevents) |
| ---------------------- | ---------------------------------------------------------- | ----------------------------------- |
| Config templating      | `Render(schema, instanceParams) -> write path P format F`  | Every per-game config file; also used by recipes. Formats: INI/JSON/KV/XML. |
| SteamCMD               | `+login anonymous +app_update <appid> validate +quit`; buildid compare for update-detect | Every game install AND every game update; all our servers are anonymous dedicated. |
| RCON                   | `Connect(host,port,pw).Exec(cmd) -> response`              | Stats, graceful shutdown, quiesce-before-backup, drain-before-host-reboot, admin actions. Highest leverage; build early and solid. |
| File-set backup        | `paths/globs + exclude + optional pre-backup quiesce(RCON)`| Every game's save-backup; one code path, per-game data. |
| Service control        | `IServiceController` (DBus/busctl on Linux, SCM on Windows)| Restarts, recipe service definitions, readiness inputs. |
| Package / "what"       | `IUpdateProvider` with >=2 real backends (apt AND Plex)    | Every update kind; the non-apt backend ships day one so it cannot ossify apt-only. |
| Readiness              | log-scrape | port-probe | query-protocol (a2s, etc.)      | Game-level "accepting players"; readiness != process-alive. |
| Idempotent script runner | ordered steps, each with alreadyDone + success check     | The one genuinely new primitive; makes recipes convergent. |

Plugin escape hatch: a rare bespoke capability (weird binary save format, custom admin
API) implements the same `ICapability` interface a primitive-backed capability does. It is
isolated and rare, and the rest of the system cannot tell the difference.

Testability: the seam-primitives that reach real infra - RCON client, file-set/S3 client,
package/"what" providers, and `ILibvirtHost` - each ship an in-memory fake (per section 3's
constraint). The pure-logic primitives (config templating, buildid comparison, the script
runner's plan/converge logic) are directly unit-testable with no fake needed.

---

## 8. Identity and auth (controller-owned, seam left)

Root of trust is centralized; work is decentralized. The controller mints, stores, and
pins per-agent identity; the agent receives it at bootstrap; rotation and revocation are
controller actions. Only trust is centralized, not labor.

### 8.1 Mechanism

- Transport: mTLS over the gRPC bidi stream. The controller runs a private CA
  (a self-managed root key held only in the controller's precious state).
- Enrollment: at registration the controller issues a per-agent client certificate and
  records its sha256 fingerprint in `agent_identity` (status `pending` until first
  successful `Hello`, then `active`). The controller pins that fingerprint; a stream
  presenting any other cert for that identity is rejected.
- Bootstrap (getting the first cert onto a node): a one-time, short-TTL enrollment token.
  New VMs receive it via cloud-init; existing boxes receive it via a single one-time SSH
  (the last SSH you do). The agent uses the token once to fetch its issued cert, then the
  token is dead.
- Rotation / revocation: controller re-issues (rotate) or moves status to `revoked`. A
  revoked identity's next stream attempt is refused at the TLS layer via fingerprint pin;
  no CRL distribution needed because the controller is the only verifier.

### 8.2 The operator-facing side

The UI talks only to the controller over its own authenticated channel (operator auth,
not agent mTLS). The UI never holds agent identities or talks to agents or libvirt
directly.

### 8.3 The seam (design for controller-owned, leave the door open)

Keep identity behind a clean interface so a future genuinely-untrusted node could hold its
own keys without re-architecting:

```csharp
public interface IAgentTrustProvider {
    Task<EnrollmentResult> EnrollAsync(EnrollmentRequest req, CancellationToken ct);
    Task<bool> VerifyAsync(PresentedIdentity presented, CancellationToken ct); // fingerprint pin today
    Task RotateAsync(string agentId, CancellationToken ct);
    Task RevokeAsync(string agentId, CancellationToken ct);
}
```

Today's implementation is `ControllerOwnedTrustProvider` (controller mints and holds the
CA). A future `AgentHeldKeyTrustProvider` could verify agent-generated keys / CSRs without
touching the callers. Design for controller-owned; leave the seam, do not build the future
case now. Per section 3's constraint, `IAgentTrustProvider` also ships an in-memory fake so
enrollment / verify / rotate / revoke flows are Tier 1 testable without a real CA.

### 8.4 Secrets in instance params

RCON passwords and similar live in `server_instance.instance_params_json`, which is
precious controller state (backed up, never on the agent of record). They are pushed to
the agent transiently as part of a job's params and are not persisted in agent-of-record
state. If you ever want to back up an agent to preserve a secret, that is the §3.9 smell:
move the secret back to the controller.
