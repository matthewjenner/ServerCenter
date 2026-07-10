# ServerCenter Workplan

Living build tracker. Functions as a todo list plus micro-plan. Keep it current: at each
phase boundary, check off done items, refresh the Current State block, and append to the
Decisions Log / Known Edges before starting the next phase (house rule:
`maintain-workplan`). ASCII punctuation only (house rule: `avoid-ai-artifacts`).

---

## Current State

- Phase: 1.5 (host as node zero) - IN PROGRESS. Phase 1 COMPLETE (protocol brain,
  pump+reconnect, real gRPC transport, SQLite persistence, identity core, mTLS enforcement,
  real-socket mTLS test). Phase 1.5a done: agent is a systemd service + generic Linux install
  package (cross-compiled + packaged here). Remaining 1.5b: release-agent.yml GitHub workflow.
- Key clarification (2026-07-10): the agent is ONE binary for host and guests. node_kind is
  just a reported label; host behavior is controller policy, not different code.
- Build status: `dotnet build ServerCenter.slnx` clean (0 warnings, TreatWarningsAsErrors
  on); `dotnet test` green (57 Core + 25 Controller + 5 integration = 87). Plus
  `Scripts/publish-agent.sh linux-x64` cross-compiles + packages the self-contained agent
  tarball (verified: Linux ELF + install assets).
- Phase 1 progress (see the Phase 1 entry below for the sub-step tracker):
  - [x] Protocol brain, transport-agnostic + Tier 1 tested: Hello/HelloAck handshake with
    version negotiation and clean Goodbye(VERSION_MISMATCH) rejection; the resync
    reconciler (the top refactor trap, every rule pinned); LivenessTracker (Online/Stale/
    Offline from heartbeat gaps); driven end-to-end over a new InMemoryDuplexLink.
  - [x] Steady-state connection pump + reconnect: AgentSessionPump (heartbeat + status on a
    TimeProvider cadence, command dispatch), ControllerSessionPump (ingest heartbeat/status/
    progress/result to a sink), BackoffPolicy (full-jitter exponential), AgentConnection
    (dial -> handshake -> pump -> reconnect-with-backoff, stops on terminal Goodbye). Tier 1
    tested with FakeTimeProvider; AgentHandshakeResult gained a Terminal flag.
  - [x] Real transport: GrpcAgentTransport (agent, owns the channel, serialized writes) +
    GrpcTransportConnector dial factory; AgentLinkService.Connect adapts the server streams to
    IControllerStream via GrpcControllerStream and drives handshake + ControllerSessionPump.
    Controller wires in-memory AgentPresenceStore (IControllerSessionSink) + InMemory
    ControllerJobView + h2c Kestrel on :5080; agent Program runs the dial loop. New
    ServerCenter.Integration.Tests project: real gRPC bidi over an in-process TestServer
    (handshake + heartbeat/status reach the controller). AgentConnection now disposes the
    transport per session. Live two-process run left to the user's own smoke (per working
    style); in-process real-gRPC path is covered by the integration test.
  - [x] SQLite persistence: ServerCenterDatabase (WAL + user_version migrations, schema V1 =
    agent_identity/node/job/job_log from phase-0-contracts.md 2.4); JobRepository (insert/get/
    open-jobs-by-agent/state transitions/log+ack) and AgentNodeRepository (idempotent upserts);
    SqliteControllerJobView replaces the in-memory placeholder; AgentLinkService registers the
    connecting agent/node; DB initialized at controller startup. Live presence stays in-memory
    (transient, not precious). New ServerCenter.Controller.Tests (real temp-file SQLite, incl.
    a survives-restart test); integration test now uses an isolated temp DB. Class tables
    (descriptors/policies/recipes/instance-params) + backup_snapshot land as later migrations
    with their features.
  - [x] Identity core (security-critical half of mTLS): ControllerOwnedTrustProvider
    (IAgentTrustProvider) over a real private CA (CertificateAuthority: RSA CA + client-cert
    issuance, sha256 fingerprint). Enroll gated by a one-time, expiring bootstrap token
    (only the hash stored); verify pins the fingerprint (accepts pending/active, rejects
    revoked/unknown/mismatch); rotate re-pins; revoke stops verify; MarkActive flips
    pending->active. Persisted via TrustRepository + schema V2 (controller_ca, bootstrap_token);
    CA ensured at controller startup. Tested with real crypto (X509Chain custom-root validation)
    + real SQLite. EnrollmentResult enriched to carry the agent key + CA cert.
  - [x] mTLS transport ENFORCEMENT (the wire half): Kestrel HTTPS with a CA-signed server cert
    + ClientCertificateMode.AllowCertificate; AgentAuthorizer (CN-bound + fingerprint-pinned,
    flips pending->active) enforced in AgentLinkService; token-gated `/enroll` endpoint (no
    client cert); agent EnrollmentClient + AgentCertStore + Program bootstrap (enroll once,
    persist, dial mTLS) with GrpcTransportConnector TLS. Gated by Security:RequireClientCertificate
    (true by default; tests over TestServer set it false). ControllerHost extracts the shared
    wiring. Proven by a real-socket mTLS integration test (real Kestrel, ephemeral port):
    enrolled agent connects + heartbeat lands; a no-client-cert connection is kicked with
    Goodbye(Revoked). Phase 1 DONE.
- Solution (`ServerCenter.slnx`, Title Case folders, Central Package Management, .NET 10):
  Contracts (the .proto wire), Core (job model + state machine + all seam interfaces +
  IAgentTransport + ProtocolVersion), Primitives (ConfigTemplateRenderer), Capabilities,
  Agent (+ Linux/Windows stub impls), Controller (mapped AgentLink gRPC endpoint), Ui
  (Avalonia 12 shell). Tests: TestFakes (InMemoryAgentTransport, FakeServiceController) +
  Core.Tests.
- Repo: local git initialized, remote `origin` set to
  https://github.com/matthewjenner/ServerCenter (public; no secrets or doxxable content;
  `.gitignore` excludes secrets/certs/db). User makes all commits (nothing committed yet).
- Docs: `architecture.md`, `phase-0-contracts.md`, `backup-restore-runbook.md`,
  `testing.md`, `build-and-update.md`, this workplan.
- Build/versioning/update foundation landed: `Directory.Build.props` VersionPrefix 0.1.0
  (lockstep, single source of truth); `Scripts/bump-version.sh` + `Scripts/run.sh`
  (ui|controller|agent); `.github/workflows/ci.yml` (matrix Linux+Windows build+test,
  non-publishing, actions pinned to verified-latest checkout@v7 / setup-dotnet@v5); UI is
  Velopack-ready (`VelopackApp.Build().Run()` + package). The three PUBLISH tracks
  (release-ui / release-agent / image-controller) are specified in `build-and-update.md` and
  DEFERRED by decision (2026-07-10) until the first usable milestone (~1.0.0); registry for
  the controller image confirmed as GHCR. Only `ci.yml` runs until then.
- Next: Phase 1.5b - the `release-agent.yml` GitHub workflow so the agent tarball installs
  straight from a GitHub release (linux-x64, version-gated; un-defers the agent publish track).
  Then the user can smoke-test installing node zero on the real hypervisor. After that: Phase 2
  (Avalonia dashboard).

## Standing conventions (decided)

- .NET 10, Avalonia (UI), gRPC bidi (contract-first `.proto`), SQLite (controller), S3
  (backup). libvirt via local mounted socket.
- Solution: `ServerCenter.slnx` (XML solution format). Folders Title Case
  (`Src`, `Tests`, `Contracts`, `Docs`).
- Transport auth: mTLS, controller as private CA (identity is controller-owned; seam left
  for a future agent-held-key provider).
- Packages: newest stable (`prefer-latest-packages`). Avalonia is on 12.x (12.1.0) - the
  inherited 11.x pin was stale, its DataGrid blocker is resolved (DataGrid ships in lockstep
  with core 12.x). Test assertions use AwesomeAssertions (FA >= 7 is paid). Central Package
  Management in `Directory.Packages.props`; re-verify on any bulk dependency bump.
- Windows-side `.ps1` build/deploy scripts must be PowerShell 5.x compatible (ASCII only,
  no `??`) - relevant from Phase 8.
- Testing / structure / style follow the house rules; each shipped step is
  build-test-deploy-doc with a terse summary, then await direction (`working_style`).

---

## Phase-by-phase plan

Each phase lists: contracts touched, primitives built or reused, and a definition of done
(DoD). Order is fixed by the brief: contracts first, read before write, Ubuntu before
Windows, reuse before bespoke.

### Phase 0 - Contracts and foundations (design before code)
- Contracts: protobuf envelope (versioned); job state machine + SQLite schema; agent
  interfaces (`IServiceController` / `IUpdateProvider` / `IProcessInspector` /
  `ISystemInfo`); the three day-one schemas (game capability/descriptor + `ICapability`,
  `UpdatePolicy`, `BuildRecipe`); identity model (controller-owned, seam); persistence =
  SQLite; backup = consistent snapshot -> versioned S3.
- Primitives: enumerated and contract-sketched (not implemented) in `phase-0-contracts.md`.
- Test tiers: none run yet. This phase DECIDES the two testability constraints
  (`testing.md`): every external-effect seam ships a fake; the transport is injectable for
  a chaos layer. Both are code-shape requirements, not later wiring.
- DoD: design docs reviewed and agreed; the fake-per-seam and injectable-transport
  constraints are baked into the interface definitions. No feature code before this gate.

### Phase 1 - Controller + Ubuntu agent, bidi stream, read-only
- Contracts touched: envelope, Hello/HelloAck handshake, Heartbeat, NodeStatus, the full
  job persistence schema, and the resync handshake scaffolding (built BEFORE any real job
  exists; top refactor trap). Controller-owned identity/registration + mTLS. Auth threaded
  in here, not bolted on later.
- Primitives: none of the game primitives yet; build the job spine, the stream lifecycle,
  reconnect-with-backoff, and the resync reconciliation rules.
- Test tiers: Tier 1 primary (bulk) - resync-across-disconnect, envelope version-skew
  rejection, job state machine, controller-restart-with-job-in-flight recovery, all driven
  in-process with the transport chaos layer; Tier 2 smoke - one real Linux agent opening a
  real stream.
- DoD: an Ubuntu agent enrolls, opens the stream, heartbeats, reports status; killing and
  restarting either side reconnects and passes an (empty) resync cleanly; job tables exist
  and the resync reconciliation is unit-tested (Tier 1) against simulated disconnects and
  envelope version-skew.

### Phase 1.5 - Host as node zero
- Contracts touched: added `node_kind` (guest|host) to Hello (additive) so the controller
  records which node is the host. It is only a LABEL the agent reports (env
  SERVERCENTER_NODE_KIND); the SAME binary runs on host and guests. Host-specific behavior is
  controller-side policy (later), not agent code.
- Primitives: reuse everything from Phase 1.
- Test tiers: Tier 1 - node_kind flows agent->controller (added). Tier 2/3 (real host agent
  over real systemd, host facts) is the user's own smoke on the real Linux hypervisor - I
  develop on Windows and cross-compile.
- Sub-steps:
  - [x] 1.5a - agent as a Linux systemd service + generic install package. Agent refactored
    to Generic Host + BackgroundService + UseSystemd (Type=notify, graceful SIGTERM, journald);
    AgentOptions/AgentBootstrap/AgentWorker. Deploy assets (`Deploy/`: unit, env template,
    install.sh, README) + `Scripts/publish-agent.sh` producing a self-contained single-file
    linux-x64 tarball (verified: cross-compiles to a Linux ELF + packages here; ~73MB). node_kind
    threaded (AgentIdentity/Hello/ControllerHandshakeResult -> EnsureNode). The package is
    GENERIC (every Linux node uses it); node zero just sets NODE_KIND=host.
  - [ ] 1.5b - `release-agent.yml` GitHub workflow so the tarball installs straight from a
    GitHub release (the user wants this; un-defers the agent publish track from build-and-update.md).
- DoD: the same agent runs natively on the hypervisor host via systemd (installed from the
  package), enrolls, and reports host facts/health through the identical interfaces, recorded
  as a `host` node. No special host subsystem.

### Phase 2 - Avalonia live dashboard
- Contracts touched: read-only consumption of NodeStatus + libvirt truth (libvirt itself
  lands in Phase 6; until then VM-running shows `Unknown`).
- Primitives: none new. UI is a thin view onto the controller.
- Test tiers: Tier 1 - dual-truth state reconciliation as view-model tests over fake
  status/libvirt feeds (Stale/Unknown transitions, disagreement cases).
- DoD: dashboard shows guest + host state with dual-truth (agent-online AND VM-running
  shown separately; `Stale`/`Unknown` first-class). Solves the headline pain point.
- Package note: Avalonia 12.1.0 (see Standing conventions).

### Phase 3 - First jobs (simplest first)
- Contracts touched: `Command` / `JobProgress` / `CommandResult` / `CancelJob`; the full
  job lifecycle end to end.
- Primitives: Service control (`IServiceController`, Linux via DBus/busctl).
- Test tiers: Tier 1 primary - full job lifecycle including mid-run disconnect and resync
  against a fake `IServiceController`; Tier 2 - real systemd service restart in an
  init-capable container.
- DoD: restart a systemd service as a persisted job; progress + log stream to the UI; the
  job survives a mid-run disconnect and resyncs correctly (exercises the whole spine).

### Phase 4 - Ubuntu updates as policy-driven jobs
- Contracts touched: `UpdatePolicy` execution; job params for update plans.
- Primitives: Package/"what" provider with AT LEAST apt AND Plex backends (proves the
  abstraction is not apt-only). Reuse: service control (stop-update-start), job spine.
- Test tiers: Tier 1 - policy resolution (how/when/reboot/preflight/approval) over fake
  providers; Tier 2 - real apt AND real Plex updates plus network-chaos on the job stream.
- DoD: neuter unattended-upgrades on onboarding; run an apt update and a Plex update as
  policy-driven jobs, each expressing how/when/reboot/preflight/approval as pure data.

### Phase 5 - Game-server capability layer (SteamCMD)
- Contracts touched: game descriptor + `ICapability` (configGen/saveBackup/stats/shutdown/
  readiness); server_instance instance params.
- Primitives: Config templating, RCON (build early and solid), File-set save-backup,
  Readiness (log-scrape/port-probe/query-protocol), SteamCMD. Reuse: service control, job
  spine, S3 mediation.
- Test tiers: Tier 1 - config templating render, descriptor/instance-param resolution, RCON
  client logic against a fake, save-backup path selection against a fake S3 client; Tier 2 -
  real anonymous SteamCMD install, RCON against a real running dedicated-server process,
  real file-set backup, real readiness probe.
- DoD: stand up an anonymous SteamCMD dedicated server driven by a descriptor; config is
  templated from instance params; readiness reflects game-level "accepting players" (not
  process-alive); save-backup writes to its own S3 path with optional RCON quiesce.

### Phase 6 - libvirt (local): read + VM lifecycle
- Contracts touched: VM-lifecycle plane surfaced into the model alongside agent state
  (dual-truth becomes real); `ILibvirtHost` wrapper.
- Primitives: none game-side. Build `ILibvirtHost` over the local socket / virsh with
  structured XML I/O, swappable.
- Test tiers: Tier 1 - libvirt XML/command generation against a fake `ILibvirtHost`; Tier 3
  - real define/start/stop against a nested-virt libvirt sandbox. NO Tier 2: libvirt is
  invisible to containers by definition (fidelity boundary in `testing.md`).
- DoD: list / dominfo / dumpxml via the local socket feed VM-running truth; start / stop /
  restart the domain as controller-driven jobs. Cheap because local.

### Phase 7 - Provisioning + build recipes
- Contracts touched: `BuildRecipe` execution; the provisioning -> managed handoff
  (`node.lifecycle`: provisioning until agent check-in, then managed).
- Primitives: Idempotent ordered script runner (the one genuinely new primitive). Reuse:
  config templating, SteamCMD, package provider, service control, file-set restore.
- Test tiers: Tier 1 - recipe planning and convergence logic against fakes; Tier 2 -
  idempotency against deliberately-dirtied containers (recipe repairs/converges); Tier 3 -
  cloud-init first boot and the provisioning->managed handoff on a real VM.
- DoD: base image + cloud-init + libvirt define/start brings up a generic managed-but-empty
  node; applying recipe vN with instance params yields a running server; re-applying to a
  half-built/drifted box converges (build = repair = rebuild). Full rebuild-from-nothing
  drill passes (backup runbook 4.3).

### Phase 8 - Windows agent
- Contracts touched: none new; implement the Phase 0 interfaces for Windows.
- Primitives: `IServiceController` via `System.ServiceProcess`/SCM; `IProcessInspector`,
  `ISystemInfo`, config templating for Windows paths/formats.
- Test tiers: Tier 1 + Tier 2 - interface-conformance (the platform abstraction holds, same
  shape as Linux; container-test only the conformance); Tier 3 secondary - session-0
  service/SCM semantics that a Windows container cannot represent. Windows Update is NOT here
  (Phase 9). Fidelity boundary: Windows containers != Windows VMs.
- DoD: a Windows node enrolls and reaches parity with Ubuntu for status, service control,
  and any already-shipped game primitives, against the now-proven interfaces.
- Note: any new `.ps1` must be PowerShell 5.x compatible (house rule).

### Phase 9 - Windows updates + remaining specifics
- Contracts touched: `UpdatePolicy` with a Windows Update "what" provider.
- Primitives: Windows Update provider (PSWindowsUpdate or WUA COM). Budget disproportionate
  time: session-0 quirks, heavier reboots.
- Test tiers: Tier 3 ONLY, on a real Windows VM. Non-negotiable per the fidelity boundary:
  Windows Update does not work in a container (no usable WUA, faked reboots, session-0
  divergence). Reserve the real Windows VM for Update, reboots, and session-0 services.
- DoD: Windows updates run as policy-driven jobs with correct reboot handling.

### Phase 10 - Agent self-update + watchdog
- Contracts touched: a self-update job type; the phone-home-within-N-seconds proof.
- Primitives: blue-green slot flip; watchdog rollback. Reuse: job spine.
- Test tiers: Tier 1 - blue-green slot-flip and the rollback decision (phone-home-within-N)
  against fakes; Tier 3 - watchdog surviving a real process/host restart on a real VM
  (containers fake restarts poorly).
- DoD: agent updates itself as a job (two slots, flag/symlink flip); a new binary that
  cannot phone home within N seconds is rolled back automatically; SSH remains break-glass.

## Cross-cutting (from the start)
- Auth/identity: Phase 1, not bolted on later.
- Job model: everywhere.
- Versioned contracts: Phase 0.
- Backup/restore: stand up early and test the restore, not just the backup.
- Testing (`testing.md`): three-tier pyramid matched to fidelity ceilings. Two
  code-shape constraints decided in Phase 0 - every external-effect seam ships a fake, and
  the transport is injectable for a chaos layer. Tier 1 is the per-commit gate; Tier 2/3
  gate at CI / nightly / on-demand. Plan the thin Tier-3 layer alongside the container tier.

---

## Known Edges (watch these; they are the refactor traps)

- Resync-across-disconnect is the top refactor trap; scaffold it in Phase 1 before any real
  job (done in the design; enforce in code).
- Readiness != process-alive; health must be game-level.
- Idempotent recipe steps: the difference between reliable and pristine-only.
- Windows Update: awkward, session-0, slow reboots; disproportionate time (Phase 9).
- Host reboot: takes down controller + all guests; special preflight + expect-to-reconnect;
  the controller cannot observe its own return.
- Consistent SQLite snapshot: never copy the live file; `VACUUM INTO`; test the restore.
- "What"-provider abstraction must ship a non-apt case (Plex) early or it ossifies apt-only.
- libvirt from .NET: no good first-party binding; local socket / virsh with structured XML,
  wrapped behind `ILibvirtHost` so it is swappable.

## Decisions Log

- 2026-07-09: Contract-first `.proto` (not code-first) for the versioned wire.
- 2026-07-09: mTLS with controller-as-private-CA for agent identity; seam
  (`IAgentTrustProvider`) left for a future agent-held-key provider.
- 2026-07-09: SQLite (WAL), backed up via `VACUUM INTO` -> versioned S3, IAM scoped to one
  bucket, creds injected at runtime, lifecycle-expire old versions.
- 2026-07-09: Solution format `.slnx`; folders Title Case; repo public at
  matthewjenner/ServerCenter; user makes all commits.
- 2026-07-09: Avalonia on 12.1.0. The inherited "stay on 11.x" pin was re-verified and
  found stale for this greenfield project - the DataGrid-lagging blocker is resolved
  (DataGrid 12.1.0 ships in lockstep with core). AwesomeAssertions for assertions (FA >= 7
  is paid). Central Package Management via Directory.Packages.props.
- 2026-07-09: Three declarative surfaces stored as versioned JSON keyed by (id, version);
  running server_instance pins exact versions for exact history reconstruction.
- 2026-07-09: Testing is a three-tier pyramid (`testing.md`). Two testability constraints
  are Phase-0 code-shape requirements: fake-per-seam and injectable transport. xUnit +
  AwesomeAssertions/FA-6.12.x. Tier 1 per-commit; Tier 2 Podman/systemd + network chaos;
  Tier 3 real (nested-virt) VM for libvirt lifecycle, cloud-init, reboots, Windows Update.
- 2026-07-09: Phase DoDs live in `workplan.md` (single living tracker), not a `Docs/phases/`
  tree; each DoD now names its test tier(s).
- 2026-07-09: Scaffold landed - 11 projects, `.slnx`, CPM, xunit.v3 + AwesomeAssertions,
  contract-first .proto compiling via Grpc.Tools. Build clean under TreatWarningsAsErrors;
  23 tests green.
- 2026-07-09: Security pin - Microsoft.Data.Sqlite 10.0.9 pulls the vulnerable native lib
  SQLitePCLRaw.lib.e_sqlite3 2.1.11 (NU1903, GHSA-2m69-gcr7-jv3q). The 2.x line has no fix,
  so transitive-pinned the native lib to 3.53.3 (patched SQLite, ABI-compatible). Revisit
  when Microsoft.Data.Sqlite adopts SQLitePCLRaw 3.x. Documented in Directory.Packages.props.
- 2026-07-10: Phase 1.5a - agent runs as a Linux systemd service and ships as a GENERIC
  self-contained install package (same package for host + guests). Refactored to Generic Host +
  UseSystemd. Added node_kind (guest|host) as a reported LABEL only - NOT a special agent; host
  behavior is controller policy. Dev is on Windows, so the agent is cross-compiled to linux-x64
  and the real install/run is the user's smoke on the hypervisor. publish-agent.sh builds the
  tarball; artifacts/ is gitignored.
- 2026-07-10: mTLS transport enforcement landed (closes Phase 1). Controller runs HTTPS with a
  CA-signed server cert (regenerated at startup, agents trust via the CA) + AllowCertificate;
  per-connection authorization (CN-bound + fingerprint pin) in AgentLinkService, not TLS-layer,
  so the token-gated /enroll endpoint can share the port certless. Enforcement is gated by
  Security:RequireClientCertificate (default true; TestServer-based tests set false since
  TestServer has no TLS). The real-socket test builds real Kestrel on an ephemeral port via the
  extracted ControllerHost. Agent auto-enrolls once, persists to a local cert dir (gitignored),
  and dials mTLS. Enrollment server-trust is TOFU for now (CA fingerprint should be pinned
  out-of-band in production).
- 2026-07-10: Identity core / mTLS split into two ships - the security-critical trust logic
  (CA, mint, fingerprint pin, enroll, rotate, revoke) landed and fully tested with real crypto;
  the wire-level TLS enforcement is a separate follow-up needing real-socket tests. Controller
  is its own private CA (RSA, self-signed). CA private key stored in SQLite (precious, backed up);
  encryption-at-rest of the key is deferred hardening. Bootstrap tokens are one-time + expiring,
  only the sha256 hash stored. Fingerprint = sha256 over cert DER. Until the transport ship, the
  dial is still plaintext h2c and the trust core is NOT yet enforced on the wire.
- 2026-07-10: SQLite persistence. Raw Microsoft.Data.Sqlite (no ORM) - own the SQL, WAL mode,
  user_version migrations, per-connection foreign_keys. Job state stored as stable lowercase
  text (decoupled from enum names). Precious state only (jobs/identities); live presence stays
  in-memory per the one-backup invariant. Schema V1 = the four spine tables; class/instance/
  backup tables come as later migrations with their features (avoid dead schema). Tested
  against real temp-file SQLite incl. a survives-restart test.
- 2026-07-10: Real gRPC transport wired. Dev uses plaintext HTTP/2 (h2c) on :5080 with the
  SocketsHttpHandler.Http2UnencryptedSupport switch on the agent; TLS/mTLS replaces it in the
  identity ship. Transport adapters serialize writes (gRPC forbids concurrent stream writes)
  ahead of Phase 3's progress+heartbeat concurrency. Verified via an in-process real-gRPC
  integration test (WebApplicationFactory TestServer); live two-process runs are the user's
  own smoke per working style.
- 2026-07-10: Phase 1 approach - build the protocol brain (handshake, version negotiation,
  resync reconciliation, liveness) as pure, transport-agnostic Core logic tested at Tier 1
  over an in-memory duplex link BEFORE wiring real gRPC/SQLite/mTLS. Realizes the fake-per-
  seam + injectable-transport constraints and de-risks the top refactor trap first. Handshake
  is split from the steady-state pump (separate ships). Added IControllerStream (controller
  end of the stream) mirroring IAgentTransport.
- 2026-07-10: Versioning + update model (`build-and-update.md`). Single lockstep VersionPrefix
  (decoupled from the wire protocol version). THREE distinct update models, not one: UI =
  Velopack (mirrors the sibling Avalonia apps); Agent = blue-green self-update + watchdog
  (brief 3.14 / Phase 10, NOT Velopack), multi-RID bundles distributed via the controller;
  Controller = container image to GHCR. Adopted the siblings' bump-version.sh + run.sh +
  version-gated release pattern. GitHub Actions pinned to verified-latest majors (checkout@v7,
  setup-dotnet@v5, docker/*@current) - the siblings' @v4 pins are stale post Node 24 runner.
