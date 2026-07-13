# ServerCenter Workplan

Living build tracker. Functions as a todo list plus micro-plan. Keep it current: at each
phase boundary, check off done items, refresh the Current State block, and append to the
Decisions Log / Known Edges before starting the next phase (house rule:
`maintain-workplan`). ASCII punctuation only (house rule: `avoid-ai-artifacts`).

---

## Current State

- LATEST (2026-07-11) - post-Phase-7 PRODUCT HARDENING. The real hardware is LIVE and self-managing:
  the hypervisor (node zero) + 4 guests (plex/satisfactory/web/torrent), all on the same auto-updating
  version. Delivered on top of Phases 0-7 (version 0.1.9):
  - DEPLOY: agent installs from its GitHub release; controller is a GHCR image. `install.sh
    --with-controller` stands up node zero (controller container + host agent) in ONE command; guests
    use `install.sh --controller <host-or-url>` (auto-configures + starts, no env hand-editing).
  - AUTO-UPDATE (interim, pre-Phase-10, no rollback): CONTROLLER-DISTRIBUTED. The controller bakes agent
    bundles into its image + serves GET /agent/version + /agent/bundle/{rid}; each node's
    servercenter-agent-update.timer pulls a newer bundle FROM THE CONTROLLER (never GitHub) and
    swaps+restarts; the controller self-updates via docker compose pull. Cadence ~5min (dev), earmarked
    to become a controller-managed setting. See [[build-and-update-model]].
  - DIAGNOSTICS: agent reports its REAL version (assembly, not the old hardcode), os/arch,
    reboot-pending, and live CPU/mem/disk WITH absolute totals (mem/disk bytes, cpu cores) via
    ISystemInfo/LinuxSystemInfo. Surfaced in NodeState + the UI; controller version shown too.
  - OPERATOR API + UI: store/list endpoints for game-descriptors, build-recipes, server-instances, and
    GET /update-policies - RESOLVES the "sqlite3 seeding" gap. A default `apt` update policy is seeded
    on startup so the policy picker is never empty.
  - DISCOVERY/AUTOMATION (the "stop typing opaque ids" arc): agent enumerates systemd services -> GET
    /nodes/{id}/services -> UI service picker; libvirt VM AUTO-LINK (LibvirtAutoLinker matches unlinked
    guest nodes to same-named domains every 30s) + GET /libvirt-domains override; policy dropdown.
  - UI is now a CARD-based dashboard (was table -> tabs -> cards): Fleet tab = a responsive card grid
    (WrapPanel), one card per node, with per-card actions (VM start/stop/restart on guests, service
    restart, update) - actions moved ONTO the card (no select-then-act). Servers + Jobs tabs; a
    persistent connection header with a saved controller-address setting; UI version in the title bar.
    UI ships as a VELOPACK release (v0.1.13): release-ui.yml packs a self-contained win-x64 build with
    `vpk pack` and publishes it as a `ui-v<version>` GitHub release; the installed app self-updates via
    UpdateService (Velopack GithubSource) with an in-app "update available" banner. No longer run-from-
    source-only. See [[build-and-update-model]].
  - FIXED (2026-07-11, v0.1.10): THE AGENT NOW RUNS AS ROOT. apt/systemctl/reboot jobs were failing on
    every node (`Could not open lock file .../lock (13: Permission denied)` + `Read-only file system
    (30)`) because Deploy/servercenter-agent.service ran `User=servercenter` with `NoNewPrivileges=true`
    + `ProtectSystem=strict` - an unprivileged, read-only-/var posture that fundamentally conflicts with
    the agent's job (manage the node: apt, systemctl, SteamCMD -> /opt, reboot, write configs). Fix:
    dropped User/Group + NoNewPrivileges + ProtectSystem/ProtectHome/PrivateTmp from the unit (runs as
    root, the standard infra-agent posture); removed the `servercenter` useradd + chowns from install.sh
    and the updater. The updater already swaps the unit + daemon-reloads, so this lands on live nodes via
    auto-update. Targeted hardening (ReadWritePaths/capabilities) can be layered back later. See
    [[agent-privilege-gap]].
  - FIXED (2026-07-11, v0.1.11): `apt-get update` is now BEST-EFFORT. One misconfigured/unreachable
    third-party repo (torrent-desktop's stale ExpressVPN `mirror+file:` source, "does not have a Release
    file") made `apt-get update` exit 100, and the provider ABORTED before ever running the upgrade -
    so the whole node went unpatched and the job showed a misleading `Failed`. Now AptUpdateProvider logs
    the refresh error as a warning and presses on to `apt-get upgrade` (using whatever indices DID
    refresh); the upgrade's exit code decides the job, and a successful run whose refresh had errors
    surfaces `updated (some repos failed to refresh: ...)` in the job Detail. There is no apt flag to
    skip one bad repo mid-run - this is the right layer to be tolerant.
  - DONE (2026-07-11, v0.1.12): (a) TOKEN-MINT endpoint - `POST /enroll-token` (operator) exposes the
    existing ControllerOwnedTrustProvider.CreateBootstrapTokenAsync (one-time, hashed, TTL-clamped
    token), closing the last end-to-end gap so `/enroll` finally has an operator step. (b) SETTINGS TAB -
    the controller URL + Connect moved off the always-visible header (now a slim status strip) into a
    Settings tab that also mints enrollment tokens and manages update policies ("profiles"); the "Store
    policy" affordance moved here out of the Servers tab.
  - STILL OPEN: mTLS is still plaintext h2c in the bring-up (enroll works, but the transport isn't
    switched to https/mTLS by default yet); the pending->approve trust model (decided, not built - see
    [[trust-onboarding-model]]); absolute-value telemetry is DONE; manual VM-link UI was dropped from the
    card (auto-link covers same-named domains; add back if names differ).
  - NEXT UI DIRECTION (decided 2026-07-11 with user): the Servers tab becomes a GAME-SERVER SECTION
    nested under its host node - a ServerInstance already binds NodeId + descriptor/recipe/policy, so
    surface RCON console / config editor / SteamCMD install / service-restart per instance, generic over
    the descriptor's declared capabilities (data-over-code). Its own design pass, after this slice. See
    [[game-server-section-direction]].

- Phase: 7 (provisioning + build recipes) - DoD MET at the engine/handoff level (7a-7d). BuildRecipe
  surface; idempotent script runner; recipe.apply engine (packages->SteamCMD->config->scripts->systemd
  unit, convergent, proven end-to-end); provisioning->managed handoff (a node recorded 'provisioning'
  with its libvirt_domain flips to 'managed' on its agent's first check-in, closing the Phase 6 loop).
  REMAINING as Tier-3 real-VM work: cloud-init first boot + libvirt define/start of a base image (the
  actual VM bring-up); ILibvirtHost has no Define yet.
- Deployment packaging landed (2026-07-10): `Deploy/controller/Dockerfile` (multi-stage, .NET 10
  Ubuntu base + libvirt-clients/virsh, runs as root for socket access) + `docker-compose.yml` (mounts
  the libvirt socket, volumes for the SQLite DB + templates, defaults to plaintext h2c :5080 for the
  first bring-up) + `.dockerignore`. Fixed Program.cs to bind `ListenAnyIP` not `ListenLocalhost` (a
  loopback bind is unreachable in a container / to remote guests; the agent validates the server by
  CA-chain not hostname, so cert subject stays localhost). Ordered smoke checklist:
  `Docs/linux-smoke-runbook.md`.
- END-TO-END GAPS surfaced by the packaging - BOTH NOW CLOSED: (1) bootstrap-token mint endpoint DONE
  (`POST /enroll-token`, v0.1.12) so `/enroll` has an operator step; (2) store endpoints for game
  descriptors / build recipes / server instances DONE (operator-API arc). Both mirror POST
  /update-policies. Remaining transport work: flip the default from plaintext h2c to https/mTLS.
- PRIORITY (decided 2026-07-10, user): get a STABLE LINUX platform working END TO END before any new
  feature phase. ALL Windows work is DEFERRED - Phase 8 (Windows agent), Phase 9 (Windows updates),
  and the WindowsServiceController stub stay parked (Windows VMs are for a rare game or two). The S3
  IObjectStore + the save-backup JOB are DEFERRED to ~1.0. So the near-term work is Linux
  Tier-2/Tier-3 smokes on the real hypervisor (node-zero install, real apt/Plex update, real systemctl
  service control, real SteamCMD install, real libvirt + recipe.apply on a real VM) + whatever
  packaging/wiring those smokes need - NOT Phase 8/9/S3.
- Phase: 6 (libvirt: read + VM lifecycle) - DoD MET (6a+6b+6c). ILibvirtHost realized; dual-truth is
  REAL (VM-running from libvirt alongside agent-online); VM start/stop/restart run as CONTROLLER-driven
  jobs (execute on the controller via local libvirt, not pushed to an agent) with a dashboard control.
  Gated on Libvirt:Enabled (dev/test = NullLibvirtHost). Real virsh is the user's Tier-3 smoke on the
  hypervisor. Next: Phase 7 (provisioning + build recipes). REMAINING as hardening: a `virsh event`
  push stream (WatchEvents polls now); libvirt DI wiring smoke on the real hypervisor.
- Phase: 5 (SteamCMD game-server capability layer) - DoD MET (5a-5f). Descriptor-driven server jobs
  work end to end: store descriptor + instance -> controller resolves -> dispatches server.install
  (anonymous SteamCMD) / server.config-apply (templates shipped inline, rendered on the agent) ->
  runs on the agent -> persists. Proven over real gRPC. Remaining as later hardening (not blocking):
  server.backup JOB wiring + a real S3 IObjectStore (the SaveBackup capability is done + Tier-1
  proven); a DB-backed config-template store (today a templates dir on the controller); a2s/
  log-scrape readiness; readiness feeding node status. Next: Phase 6 (libvirt) or Phase 7 (recipes).
- Phase: 4 (Ubuntu updates as policy-driven jobs) - COMPLETE (4a+4b+4c+4d). Updates run as
  policy-driven jobs end to end: store a policy -> controller resolves it -> dispatches update.apply
  -> agent runs preflight + provider (apt/Plex) + reboot decision -> persists; the dashboard has an
  update trigger (agent id + policy id + optional unit). Next: Phase 5 (SteamCMD + RCON) or 6
  (libvirt). Still open as later hardening (not blocking): an autonomous scheduler to FIRE
  window-eligible policies, the reboot follow-on job, reboot-pending in the fleet view, and wiring
  neuter-unattended-upgrades into onboarding.
  Phases 0-3 + 1.5 complete. Full job spine works: dispatch on the controller -> execute on the
  agent -> stream progress -> persist result; live jobs + a trigger in the dashboard; real Linux
  service control (systemctl). The dashboard smoke works via `Scripts/dev-stack.sh`.
- Dev convenience: `Scripts/dev-stack.sh` (bash) launches controller + agent + dashboard for
  smoke-testing. Scripts are always bash per the house rule.
- Key clarification (2026-07-10): the agent is ONE binary for host and guests. node_kind is
  just a reported label; host behavior is controller policy, not different code.
- Build status: `dotnet build ServerCenter.slnx` clean (0 warnings, TreatWarningsAsErrors +
  EnforceCodeStyleInBuild on - no-var/file-scoped-namespace rules are build-enforced); `dotnet test`
  green (102 Core + 51 Agent + 65 Controller + 10 integration + 8 UI + 13 Capabilities = 249).
- Code style (house rule, enforced): NO var, NO top-level statements - explicit types everywhere,
  full classes, file-scoped namespaces (.editorconfig + EnforceCodeStyleInBuild). Program is a full
  Main class.
  Plus `Scripts/publish-agent.sh linux-x64` cross-compiles + packages the self-contained agent
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
- Next: Phase 4 (Ubuntu updates as policy-driven jobs; needs the apt + Plex "what" providers) OR
  Phase 6 (libvirt, to make the dashboard's VM column real). Phase 4 extends the now-proven job
  spine with UpdatePolicy; Phase 6 is the other dual-truth half (needs the real hypervisor host).
  (Also open: smoke the real Linux job execution + install node zero on the hypervisor.)

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
  - [x] 1.5b - `release-agent.yml`: version-gated, idempotent (tag `agent-v<version>`), runs on
    ubuntu-latest, builds/tests only agent-relevant projects (not the Windows UI), packages
    linux-x64 + linux-arm64 tarballs and attaches them to a GitHub release. Un-defers the agent
    publish track (build-and-update.md updated). UI's Windows-only csproj props guarded by
    `$(OS)` so the cross-platform ci.yml Linux leg builds the whole solution cleanly.
- DoD: the same agent runs natively on the hypervisor host via systemd (installed from the
  package), enrolls, and reports host facts/health through the identical interfaces, recorded
  as a `host` node. No special host subsystem.

### Phase 2 - Avalonia live dashboard
- Contracts touched: read-only consumption of NodeStatus + libvirt truth (libvirt itself
  lands in Phase 6; until then VM-running shows `Unknown`).
- Primitives: none new. UI is a thin view onto the controller.
- Test tiers: Tier 1 - dual-truth state reconciliation as view-model tests over fake
  status/libvirt feeds (Stale/Unknown transitions, disagreement cases).
- DONE: controller `FleetView` gRPC (GetFleet + WatchFleet streaming) + testable
  `FleetSnapshotBuilder` (joins nodes with presence, derives agent-online via LivenessTracker,
  VM=Unknown). Avalonia dashboard: `DashboardViewModel` (reconciles snapshots into a DataGrid,
  resilient watch loop), `GrpcFleetClient`, `NodeRowViewModel`. Tier 1 tests: FleetSnapshotBuilder
  (Online/Stale/Offline + Unknown) + DashboardViewModel.Apply (add/update/remove). Real-gRPC
  integration test: a connected agent appears in FleetView.GetFleet as Online. UI uses reflection
  bindings (compiled-binding x:DataType is fiddly with DataGrid); operator auth + UI TLS server-
  cert validation deferred (TOFU for now).
- DoD: dashboard shows guest + host state with dual-truth (agent-online AND VM-running
  shown separately; `Stale`/`Unknown` first-class). Solves the headline pain point. MET.
- Package note: Avalonia 12.1.0 + Avalonia.Controls.DataGrid 12.1.0 + CommunityToolkit.Mvvm 8.4.2.

### Phase 3 - First jobs (simplest first)
- Contracts touched: `Command` / `JobProgress` / `CommandResult` / `CancelJob`; the full
  job lifecycle end to end.
- Primitives: Service control (`IServiceController`, Linux via DBus/busctl).
- Test tiers: Tier 1 primary - full job lifecycle including mid-run disconnect and resync
  against a fake `IServiceController`; Tier 2 - real systemd service restart in an
  init-capable container.
- Sub-steps:
  - [x] 3a - agent execution engine: `IJobExecutor` contract, `AgentJobStore` (tracks in-flight
    jobs for resync, replaces EmptyAgentJobStateSource), `TransportJobSink` (streams JobProgress
    with per-job seq), `JobExecutingCommandHandler` (dispatches Command -> executor in the
    background, sends terminal CommandResult), `ServiceRestartExecutor` (via IServiceController).
    `IAgentCommandHandler.OnCommandAsync` now gets the transport (to stream up). Wired into
    AgentWorker (picks WindowsServiceController/LinuxServiceController by OS). Tier 1 tested
    (ServerCenter.Agent.Tests): service.restart succeeds; unknown type fails.
  - [x] 3b - controller dispatch: ConnectedAgents registry (agent id -> live IControllerStream,
    registered/unregistered in AgentLinkService), JobDispatcher (insert job at Queued -> push
    Command down the stream; offline agent -> stays Queued), PersistingSessionSink (delegates
    presence, persists JobProgress -> running/pct/log/ack and CommandResult -> terminal via
    JobRepository.ApplyProgressAsync/UpdateStateAsync). Dev trigger: POST /jobs/service-restart.
    Tier 1 tests (dispatcher, sink) + a real-gRPC integration test: dispatched service.restart
    runs on the agent (fake IServiceController) and persists Succeeded. NOTE: on the Windows dev
    stack the real service controller is a stub, so a dispatched job FAILS with NotImplemented
    until 3c - the spine works, the executor does not yet.
  - [x] 3c - real Linux `IServiceController` via systemctl (structured `show --property=ActiveState`,
    not free-text status; verbs start/stop/restart/enable; WatchAsync polls - DBus PropertiesChanged
    is the future push upgrade). Behind an injectable IProcessRunner so command/parse logic is
    unit-tested on Windows (ServerCenter.Agent.Tests); real execution smokes on a Linux node.
    Wired into AgentWorker.
  - [x] 3d - UI job view: controller `JobView` gRPC (WatchJobs streaming + RestartService) +
    JobRepository.ListRecentJobsAsync; dashboard gained a jobs DataGrid (live) + a trigger row
    (agent id + unit + Restart button) via JobsViewModel/JobRowViewModel/GrpcJobClient +
    MainWindowViewModel. Tier 1 tests (JobsViewModel.Apply, ListRecentJobs newest-first).
- DoD: restart a systemd service as a persisted job; progress + log stream to the UI; the
  job survives a mid-run disconnect and resyncs correctly (exercises the whole spine).

### Phase 4 - Ubuntu updates as policy-driven jobs
- Contracts touched: `UpdatePolicy` execution; job params for update plans.
- Primitives: Package/"what" provider with AT LEAST apt AND Plex backends (proves the
  abstraction is not apt-only). Reuse: service control (stop-update-start), job spine.
- Test tiers: Tier 1 - policy resolution (how/when/reboot/preflight/approval) over fake
  providers; Tier 2 - real apt AND real Plex updates plus network-chaos on the job stream.
- Sub-steps:
  - [x] 4a - the declarative surface + its brain. `UpdatePolicy` model (Core/Updates: what/how/
    when/reboot/preflight/approval as pure data) + `UpdatePolicySerializer` (kebab-case enum tokens
    matching the brief, camelCase props, canonical body_json) + pure `UpdatePolicyResolver`
    (DecideStart: window-eligibility via Cronos, manual-override, approval gate, preflight
    dedupe/order; ResolveReboot: the Never/IfRequired/AlwaysAfter/Prompt x reboot-required matrix).
    Persistence: schema V3 `update_policy` table (versioned JSON keyed by id+version, like
    descriptors/recipes) + `UpdatePolicyRepository` (immutable revisions, GetLatest). Cronos 0.13.0
    added to CPM (pure cron eval, no infra). Tier 1: resolver + serializer (Core.Tests) + repository
    (Controller.Tests, real SQLite). No autonomous scheduler yet - window is an eligibility gate;
    the firing background service is a later enhancement.
  - [x] 4b - the "what" providers. `AptUpdateProvider` (behind IProcessRunner: apt-get update +
    apt list --upgradable parse, targeted --only-upgrade vs full upgrade, DEBIAN_FRONTEND
    noninteractive, /var/run/reboot-required probe) AND `PlexUpdateProvider` (the non-apt backend:
    HTTP manifest -> version compare vs dpkg-query -> arch/distro release select -> download + dpkg
    -i; app updates never reboot) prove the abstraction is not apt-only. New `IHttpFetcher` seam
    (+HttpFetcher over HttpClient) so Plex is testable without network. `UnattendedUpgradesNeutralizer`
    masks apt-daily{,-upgrade}.timer + unattended-upgrades.service (idempotent). IProcessRunner
    gained an env overload (apt/dpkg need DEBIAN_FRONTEND; systemctl uses the plain one).
    RecordingJobSink added to TestFakes. Tier 1 (+13 tests, Agent.Tests). Real apt/Plex is the
    user's Tier 2 smoke on a Linux node.
  - [x] 4c - execution + dispatch. `UpdateApplyExecutor` (update.apply) composes the resolved policy
    (carried as `UpdateJobParams`) with the pluggable "what" provider + the job spine: validate
    provider/preflight availability -> run ordered preflight -> optional stop-update-start service
    bracket (restart even on failure) -> provider.ApplyAsync -> ResolveReboot decision recorded (the
    actual reboot is DEFERRED - rebooting mid-job kills the agent before the terminal result; host
    reboot is its own special-policy job). Preflight is pluggable `IPreflightAction` (only Notify
    today; a policy needing an unhandled step FAILS rather than silently skipping). Controller
    `UpdateJobDispatcher` resolves the stored policy (agent never sees it) -> eligible+confirmed ->
    builds params -> dispatches (cancellable=false, requeueable=false). Endpoints POST
    /update-policies (raw-body, canonical dialect) + POST /jobs/update-apply (manual trigger).
    `UpdateJobParams`/`UpdateJson` shared dialect. Wired into AgentWorker (apt+Plex on Linux, none on
    Windows). FakeUpdateProvider + FakeServiceController.Calls added. Tier 1 (executor 7, dispatcher
    4) + a real-gRPC end-to-end integration test (+12). DoD met.
  - [x] 4d - thin UI update trigger. New JobView.TriggerUpdate gRPC (agent_id + policy_id +
    optional service_unit + packages -> dispatches via UpdateJobDispatcher as a MANUAL trigger,
    returns outcome+reason); IJobClient.TriggerUpdateAsync + GrpcJobClient; JobsViewModel gained an
    update-trigger row (agent id + policy id + optional unit + status showing dispatched/not-
    eligible/needs-confirmation). Tier 1 (JobsViewModel, +3). NOTE: reboot-pending display was
    DEFERRED out of 4d - surfacing it properly needs a NodeState proto field + fleet/status plumbing
    + real agent-side reboot detection, which belongs with the reboot follow-on work, not a thin UI
    slice.
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
- Sub-steps:
  - [x] 5a - the RCON primitive (built early per the brief - highest leverage; also back-fills
    Phase 4's deferred drain/quiesce preflights). Real Source RCON client: `SourceRconClient`/session
    over an `IRconChannel` seam so the sequencing logic (auth handshake + the empty-follow-up
    multi-packet accumulation trick) is Tier 1 testable; `RconProtocol` is the pure little-endian
    length-prefixed byte framing (round-trip tested); `TcpRconChannel(Factory)` is the thin real-TCP
    adapter (Tier 2 smoke). Constants/packet/seam live in Core so `FakeRconChannel` (TestFakes,
    scripted server incl. multi-part responses + bad-password reject) needs no Primitives dep. Tier
    1 (+7, Core.Tests). RconAuthenticationException on bad password.
  - [x] 5b - the declarative surface. `GameDescriptor` model (Core/Games: steamApp + optional
    strongly-typed capability specs configGen/saveBackup/stats/shutdown/readiness, each carrying a
    `primitive` name; matches phase-0-contracts.md 4) + `GameDescriptorSerializer` (camelCase,
    null-omit, lowercase enum tokens "kv"/"a2s", tokens preserved verbatim). `ServerInstance` model
    (instance side: pins descriptor/recipe/policy versions + opaque instance_params_json holding
    secrets). Schema V4 (game_descriptor + server_instance, node-indexed) + GameDescriptorRepository
    + ServerInstanceRepository (immutable descriptor revisions; instance round-trip + list-by-node).
    `InstanceParamsResolver` (ServerCenter.Primitives) flattens instance params JSON to dotted tokens
    ({"ports":{"game":27015}} -> "ports.game"="27015") that ConfigTemplateRenderer resolves - the
    class-vs-instance seam. Tier 1 (+10: Core 6, Controller 4). NOTE: the plugin escape-hatch
    representation (primitive -> plugin swap) is deferred to when the first plugin lands.
  - [x] 5c - the SteamCMD primitive. `ISteamCmd` seam (Core.Primitives) + `SteamCmd` (Agent.Linux,
    over IProcessRunner like apt/Plex): anonymous `+force_install_dir +login anonymous +app_update
    <id> [-beta <branch>] [validate] +quit`; success parsed from the "fully installed" marker;
    convergent (ensure-installed = install = repair = update). Update-detect via installed buildid:
    pure `SteamAppManifest.ParseBuildId` (KeyValues .acf) + a thin File.ReadAllText of
    steamapps/appmanifest_<id>.acf (best-effort, absent -> null). FakeSteamCmd in TestFakes. Tier 1
    (+7: command construction/beta/validate/failure + manifest parse). Real multi-GB install is the
    Tier 2 smoke. NOTE: querying Steam's LATEST available buildid (true "update available?" without
    downloading) is deferred - needs app_info_print parsing; buildid compare is pre/post-update for now.
  - [x] 5d - the capabilities that compose EXISTING primitives (RCON + templating). `ConfigGenCapability`
    (IConfigTemplateSource resolves schemaRef -> template; ConfigTemplateRenderer renders vs instance
    params; IConfigWriter persists to path; missing token = hard fail, no half-write). `RconStatsCapability`
    (runs the descriptor's command map, returns raw outputs; structured player-count parse is
    game-specific/later). `RconShutdownCapability` (RCON drain command, then wait the grace via an
    injected TimeProvider; the service stop is the executor's job). `RconEndpoints` resolves the
    endpoint from instance params (loopback default, ports.rcon + rcon.password, fail loudly). Seams
    IConfigTemplateSource/IConfigWriter in Core.Capabilities (+ File* real impls in Capabilities,
    fakes in TestFakes). New ServerCenter.Capabilities.Tests project. Tier 1 (+7).
  - [x] 5e - SaveBackup + Readiness (the file/network-seam capabilities). `SaveBackupCapability`:
    optional RCON quiesce -> `IFileSetArchiver.CreateArchive(paths, exclude)` -> `IObjectStore.Put`
    at the instance's OWN key `saves/{instanceId}/saves.zip` (versioned store = each backup a new
    version, a snapshot id IS a version id; game saves are data-plane on their own path, brief 3.9);
    Restore fetches a version + extracts. `ReadinessCapability`: game-level, resolves the port TOKEN
    ("{{ports.game}}") via ConfigTemplateRenderer then probes; port-probe only for now (a2s/log-scrape
    fail loudly, not pretend). New seams: IFileSetArchiver (Core.Capabilities, real ZipFileSetArchiver
    Tier 2), IPortProbe (Core.Primitives, real TcpPortProbe). Fakes: FakeObjectStore (in-memory
    versioned) + FakePortProbe + FakeFileSetArchiver. Tier 1 (+6). All 5 ICapability impls now exist.
  - [x] 5f - descriptor-driven server jobs wired into the spine. Agent executors `server.install`
    (SteamCMD EnsureApp from the descriptor's steamApp; convergent -> requeueable) + `server.config-apply`
    (ConfigGen capability; templates shipped INLINE in the job params so rendering stays on the agent,
    controller owns the data). Controller `ServerJobDispatcher` resolves instance -> pinned descriptor
    (agent never sees it), builds params (install: SteamAppRequest; config-apply: files + resolved
    template text + flattened instance params), dispatches; NotFound/NotConfigured outcomes. Endpoints
    POST /jobs/server-install + /jobs/server-config-apply. Controller now refs Primitives+Capabilities;
    IConfigTemplateSource registered as FileConfigTemplateSource(templatesRoot, default "templates",
    Templates:Root). Wired into AgentWorker (SteamCmd + FileConfigWriter). Tier 1 (executors 4,
    dispatcher 4) + real-gRPC end-to-end install integration test (+9). DEFERRED: server.backup JOB
    wiring needs a real S3 IObjectStore (capability done); DB-backed template store; a2s readiness.
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
- Sub-steps:
  - [x] 6a - the ILibvirtHost primitive. `VirshOutputParser` (PURE: parse `virsh list --all` table
    incl. multi-word states + `virsh dominfo` Name/UUID/State + the state-text -> DomainState map) +
    `VirshLibvirtHost` (a thin leaf adapter over System.Diagnostics.Process running virsh, Tier 3
    only - same pattern as TcpRconChannel; WatchEvents polls ListDomains for now; connectUri for a
    non-default libvirt). `FakeLibvirtHost` in TestFakes (seeded domains, start/shutdown/reboot
    transition state + record calls). Tier 1 (+9, parser). Chose Process-direct + pure-parser over
    promoting IProcessRunner to Core (avoids a wide refactor; parser is what needs testing).
  - [x] 6b - VM-running truth into the fleet (dual-truth becomes REAL). `LibvirtDomainStates` (in-memory
    live cache, like AgentPresenceStore) fed by `LibvirtStatePoller` (BackgroundService: seed via
    ListDomains, then follow WatchEvents); FleetSnapshotBuilder maps a node's libvirt_domain ->
    DomainState -> NodeState.vm_state (Running->Running, ShutOff/Shutdown/Crashed/Paused->Stopped,
    else->Unknown). Unknown stays FIRST-CLASS: a node with no linked domain, or one libvirt hasn't
    reported, is Unknown, never a lying Stopped (brief 3.7). Gated on Libvirt:Enabled - a NullLibvirtHost
    keeps dev/test at Unknown + fails lifecycle loudly; real VirshLibvirtHost + the poller only when
    configured. node.libvirt_domain now read (NodeRow) + settable (AgentNodeRepository.SetLibvirtDomainAsync
    + POST /nodes/{id}/libvirt-domain; provisioning will set it in Phase 7). Tier 1 (+5).
  - [x] 6c - VM lifecycle as CONTROLLER-driven jobs. `VmLifecycleService` (start/stop/restart) resolves
    a node's linked domain, persists a real vm.{start,stop,restart} job, and executes it INLINE on the
    controller against ILibvirtHost (Queued -> Running -> terminal) - the libvirt verbs return fast
    (they request the transition; the poller reflects it). NotFound / NoDomain outcomes. New JobView
    gRPC TriggerVmAction (node-keyed, VmLifecycleAction enum) + IJobClient.TriggerVmActionAsync +
    dashboard VM control row (node id + Start/Stop/Restart buttons via a parameterized RelayCommand).
    AgentNodeRepository.GetNodeAsync added. Tier 1 (VmLifecycleService 4, JobsViewModel VM 2). DoD MET.
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
- Sub-steps:
  - [x] 7a - the BuildRecipe declarative surface. `BuildRecipe` model (Core/Recipes: baseRequirements +
    steamApp + configFiles + scripts + serviceDefinition + descriptorRef, REUSING SteamAppSpec +
    ConfigFileSpec from Core.Games so recipe and descriptor share types) + `BuildRecipeSerializer`
    (reuses GameDescriptorSerializer.Options - same game dialect: camelCase, lowercase enum tokens,
    null-omit, tokens preserved). Schema V5 build_recipe (versioned JSON keyed by id+version, like
    update_policy/game_descriptor) + `BuildRecipeRepository` (immutable revisions, GetLatest). Every
    step is convergent by design (RecipeScript carries alreadyDone + onSuccess). Tier 1 (+5).
  - [x] 7b - the idempotent ordered script runner (the one genuinely new primitive). `ScriptRunner`
    (Agent.Linux, via IProcessRunner): for each step in order, if its `alreadyDone` shell check passes
    -> SKIP (convergence); else `sh -c run`, and on success `sh -c onSuccess` (the mark-done sentinel).
    A failed step STOPS the run (later steps may depend on it). Returns Executed/Skipped lists +
    FailedScriptId. All commands via `sh -c` so shell syntax works. Tier 1 (+4: skip-when-done,
    run+mark-when-not, no-alreadyDone-always-runs, stop-on-failure). Real scripts are Tier 2.
  - [x] 7c - the recipe.apply execution engine. `RecipeApplyExecutor` (Agent) composes, in order and
    all convergent: baseRequirements (`IPackageInstaller` -> `AptPackageInstaller`, apt-get install -y)
    -> steamApp (SteamCMD ensure) -> configFiles (ConfigGen, templates shipped inline) -> scripts
    (7b ScriptRunner) -> serviceDefinition (`SystemdUnitRenderer` -> write unit via IConfigWriter ->
    IServiceController.ReloadAsync [daemon-reload, NEW on the interface] -> enable -> start). A failed
    step fails+stops the job. Controller `ServerJobDispatcher.ApplyRecipeAsync` resolves the instance's
    pinned recipe + ships its templates; POST /jobs/recipe-apply. Wired into AgentWorker. New seams:
    IPackageInstaller (Core) + FakePackageInstaller; IServiceController.ReloadAsync (Linux=daemon-reload,
    Windows/Fake=noop). Tier 1 (executor 4, unit renderer 2, dispatcher 2) + a real-gRPC end-to-end
    recipe.apply integration test (+9). DoD engine met.
  - [x] 7d - provisioning -> managed handoff. `AgentNodeRepository.ProvisionNodeAsync` records a node
    'provisioning' with its libvirt_domain BEFORE its agent exists (agent_id null); `MarkManagedOnCheckInAsync`
    flips provisioning->managed + adopts the agent id (COALESCE preserves), called from AgentLinkService
    on every check-in (no-op for a normal node - EnsureNode's ON CONFLICT DO NOTHING preserves a
    provisioning row, then the flip). Key wrinkle: agent_id FK -> agent_identity, so the flip runs
    AFTER EnsureAgent creates the identity. Domain survives the handoff -> VM truth + lifecycle jobs
    work immediately (closes the Phase 6 loop). POST /nodes/provision. Tier 1 (repo 3) + a real-gRPC
    handoff integration test (+1). NOTE: the actual VM bring-up (cloud-init first boot + libvirt
    define/start of a base image) is Tier-3 real-VM; ILibvirtHost has no Define method yet.
- DoD: base image + cloud-init + libvirt define/start brings up a generic managed-but-empty
  node; applying recipe vN with instance params yields a running server; re-applying to a
  half-built/drifted box converges (build = repair = rebuild). Full rebuild-from-nothing
  drill passes (backup runbook 4.3).

### Phase 8 - Windows agent  [DEFERRED indefinitely - user decision 2026-07-10]
- DEFERRED until the Linux platform is stable + working end to end. Windows VMs are rare (a game or
  two). The WindowsServiceController stub stays as-is. Do NOT pick this up without an explicit ask.
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

### Phase 9 - Windows updates + remaining specifics  [DEFERRED indefinitely - user decision 2026-07-10]
- DEFERRED with Phase 8 (all Windows work parked until Linux is stable end to end).
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

- 2026-07-13: GAME-SERVER SECTION - SEEDED CS2 + GUIDED ADD/REMOVE UI (v0.1.18, slice 4a). SEEDED game
  data (DefaultGames.cs, mirrors DefaultPolicies, wired in Program.cs): a **CS2** GameDescriptor + a
  matching BuildRecipe, id "cs2", authored with the reserved {{instance.id}}/{{instance.dir}} tokens so
  N coexist - installDir `/opt/servercenter/cs2/{{instance.id}}`, unit `sc-cs2-{{instance.id}}.service`,
  config `{{instance.dir}}/game/csgo/cfg/gameserver.cfg`, ExecStart `.../cs2.sh -dedicated -usercon
  +game_type +game_mode +map +sv_setsteamaccount {{gslt}} +rcon_password -port {{ports.game}}` (verified
  vs current CS2 docs 2026-07: appid 730, binary game/bin/linuxsteamrt64/cs2.sh, port 27015 +2/instance).
  Ships a `templates/cs2/gameserver.cfg` template (schemaRef). UI: the Servers tab gained an **Add a
  server** form (pick game [ListGamesAsync] + node [shared live NodeIds from the fleet] + name ->
  filesystem-safe slug id + params prefilled per game) that POSTs a server-instance, and a **Remove**
  action (DELETE -> cleanup job + row). New client methods ListGamesAsync/RemoveServerInstanceAsync; the
  paste-JSON define box moved into an Expander. 311 tests green, UI render-checked. REMAINING (slice 4b):
  config-editor UI (dispatch server.config-read -> poll GET /jobs/{id}/logs -> edit -> server.config-write)
  and nested-under-nodes visual polish. Acceptance still to run on hardware: 2 CS2 on one guest. See
  [[game-server-model]].

- 2026-07-13: GAME-SERVER SECTION - CONFIG RAW READ/EDIT (v0.1.17, slice 3). New job types
  `server.config-read` (reads one rendered config path, emits the whole file as a single stdout log
  line) + `server.config-write` (writes raw content back via IConfigWriter; INDEPENDENT of config-apply,
  which re-renders from the template and would clobber a raw edit). New IConfigReader/FileConfigReader
  seam; both executors wired in AgentWorker. ServerJobDispatcher gained ConfigReadAsync/ConfigWriteAsync
  (path-guarded: refuses any path that isn't one of the instance's rendered config files, so it can't
  read/write arbitrary files) + ResolveConfigPathsAsync (the file list), all sharing a ResolveFootprint
  helper with RemoveAsync. Endpoints: POST /jobs/server-config-read|server-config-write (take instanceId,
  node derived), GET /server-instances/{id}/config-files, and GET /jobs/{id}/logs (new
  JobRepository.ListLogsAsync - surfaces persisted job output; the editor reads the read-job's stdout
  back). 306 tests green. REMAINING: slice 4 (guided UI nested under nodes: add-server form, per-instance
  Install/Config-edit/Restart/Remove, config editor that dispatches read -> polls /jobs/{id}/logs ->
  edit -> write; client list/remove/config methods; SEEDED CS2 descriptor+recipe - needs real CS2
  specifics: dedicated-server appid, ExecStart, config schema). See [[game-server-model]].

- 2026-07-13: GAME-SERVER SECTION - BACKEND (v0.1.16, slices 1-2 of an approved multi-slice plan;
  plan file shiny-gathering-finch). Goal: define games, add/remove concrete server instances, run
  MULTIPLE of the same game per VM. SLICE 1 (per-instance scoping, the linchpin): the multi-instance
  collision was three CLASS-level constants - SteamAppSpec.InstallDir, ServiceDefinition.Unit/ExecStart,
  ConfigFileSpec.Path. Fix is data-over-code: a reserved token namespace (InstanceContext -> instance.id,
  instance.name, node.id, instance.dir) rendered through the EXISTING ConfigTemplateRenderer, done
  controller-side in ServerJobDispatcher before packing job params (install/config-apply/recipe.apply).
  A descriptor/recipe authored once (installDir `/opt/servercenter/<game>/{{instance.id}}`, unit
  `sc-<game>-{{instance.id}}.service`) yields per-instance paths+units; the agent stays dumb. SLICE 2
  (remove + cleanup): new job type `server.remove` + ServerRemoveParams; new IPathCleaner/FilePathCleaner
  teardown primitive; ServerRemoveExecutor (stop+disable+delete unit best-effort -> daemon-reload ->
  delete install dir + config files, idempotent) wired in AgentWorker; ServerJobDispatcher.RemoveAsync
  renders the per-instance unit/dir/paths; ServerInstanceRepository.DeleteAsync; `DELETE
  /server-instances/{id}` (delete-after-dispatch: cleanup job dispatched, then the row is deleted - a
  failed cleanup leaves the failed job visible) + `GET /nodes/{id}/server-instances`. 300 tests green.
  REMAINING: slice 3 (config raw read/edit - server.config-read emits contents on the job log, needs a
  UI job-log surface - confirm) + slice 4 (guided UI nested under nodes, client list/remove methods,
  seeded CS2 descriptor+recipe). Acceptance = 2 CS2 instances on one guest, end to end. See
  [[game-server-model]].

- 2026-07-13: APP ICON (v0.1.15). Added a real ServerCenter icon: `Src/ServerCenter.Ui/Assets/icon.ico`
  (multi-res 16-256, generated from a transparent-PNG source via ImageMagick `icon:auto-resize`) +
  a 1024 master `icon.png`. Wired as `<ApplicationIcon>` (Windows exe/taskbar, in the Windows-guarded
  PropertyGroup) and `Icon="/Assets/icon.ico"` on MainWindow (title bar/taskbar at runtime, via a
  cross-platform `<AvaloniaResource Include="Assets/**" />`). Source PNG cleaned up from the repo root.

- 2026-07-11: WINDOW/APP STATE PERSISTENCE (v0.1.14, QoL). The UI now remembers window size, location,
  maximized state, and the selected tab across restarts, alongside the controller address - all in the
  one merged `%APPDATA%\ServerCenter\settings.json`. ConnectionSettings became the single settings
  store: every write MERGES (saving the address no longer clobbers window geometry and vice-versa);
  added UiWindowState + LoadWindow/SaveWindow. Restore/save live in MainWindow code-behind (view
  concern): size restored before show, position/maximized/tab in Opened. OFF-SCREEN GUARD (user-flagged
  trap): a window closed while MINIMIZED reports bogus position (-1 / -32000); on close we skip
  capturing geometry when minimized (keep the last good values, update only the tab), and on both save
  and restore a position is only used if it lands on a connected screen (Screens.All) - so an unplugged
  monitor or a minimized-close can never strand the window off-screen. Round-trip + merge covered by
  ConnectionSettingsTests. (Also the intended side effect: a fresh version to exercise UI auto-update.)

- 2026-07-11: UI VELOPACK AUTO-UPDATE SHIPPED (v0.1.13). The operator UI no longer has to run from
  source. release-ui.yml (windows-latest, version-gated, tag `ui-v<version>`) publishes a self-contained
  win-x64 build packed with `vpk pack --packId ServerCenter`; the installed app self-updates via a new
  UpdateService (Velopack UpdateManager over GithubSource, 5-min DEV poll - earmarked to become a
  user/controller-managed setting with a saner default - best-effort) surfaced as an in-app banner (UpdateBannerViewModel; Install disabled in dev where IsInstalled is false). Mirrors the
  sibling Avalonia apps (Klakr/FleaTrackr). The Velopack bootstrap (`VelopackApp.Build().Run()` + package)
  was already present; this adds the release pipeline + the check/apply/UI. `ui-v*` tags are distinct from
  `agent-v*`/`controller-v*` so releases never collide, and Velopack ignores the non-UI releases (no
  RELEASES/.nupkg assets). 0.1.13 is the bootstrap release - install it once, then it auto-updates.
  Action pins reuse the repo's verified checkout@v7 / setup-dotnet@v5. See [[build-and-update-model]].

- 2026-07-11: SETTINGS-TAB UX PASS (v0.1.13, from live user feedback). (1) TTL field was a TextBox bound
  to an int, so clearing it threw a raw InvalidCastException into the UI -> switched to a NumericUpDown
  (decimal?, 1-1440, FormatString "0 'min'"); the mint command falls back to 60. (2) Added hover
  TOOLTIPS across fields (Settings, Fleet card pickers/VM buttons, Servers store/action buttons) plus a
  small round "i" info badge (Window style `Border.info`) with richer help on section headers. (3) A
  COPY button on the minted token (clipboard via a `Copy_Click` code-behind handler reading the button's
  Tag - Avalonia 12's clipboard SetTextAsync is an extension in Avalonia.Input.Platform.ClipboardExtensions).
  (4) Update-policy editor made learnable: a "Load example" button inserts a valid starter policy, and
  the defined-policy chips are now buttons that LOAD that policy's JSON into the editor to view/clone
  (new IAdminClient.ListPoliciesAsync returns full bodies). FOLLOW-UP (noted, not built): a guided
  form-builder for policies (dropdowns for how/when/reboot generating the JSON) instead of raw JSON.

- 2026-07-11: TOKEN-MINT ENDPOINT + SETTINGS TAB (v0.1.12, one bundled slice per user). (a) `POST
  /enroll-token` exposes the already-existing ControllerOwnedTrustProvider.CreateBootstrapTokenAsync as
  an operator endpoint (one-time hashed token, TTL defaulted to 60min and clamped to <=24h); auth still
  deferred, same plaintext-operator posture as the other endpoints. Round-trip proven by an integration
  test (mint -> enroll -> cert bundle). This closes the last "no operator enroll step" end-to-end gap.
  (b) A Settings tab: the controller URL + Connect moved off the always-visible header (now a slim
  status strip showing address + connection state) into Settings, which also mints enrollment tokens and
  manages update policies; "Store policy" moved here out of the Servers tab. Also DECIDED with the user:
  the Servers tab will become a game-server SECTION nested under nodes ([[game-server-section-direction]]),
  and the pending->approve trust gate ([[trust-onboarding-model]]) holds until after that.

- 2026-07-11: `apt-get update` IS BEST-EFFORT (v0.1.11). On real hardware, one broken third-party repo
  (a stale ExpressVPN `mirror+file:` source with no Release file) made `apt-get update` exit 100, and
  AptUpdateProvider aborted the job BEFORE the upgrade - leaving the node unpatched under a misleading
  `Failed`. There is no apt flag to ignore a single bad repo for one run, so the provider is the right
  place to be tolerant: log the refresh error as a warning, continue to `apt-get upgrade` from the
  indices that DID refresh, and let the upgrade's exit code decide the job. Success with a failed
  refresh reports `updated (some repos failed to refresh: ...)` in the job Detail. Node-level cleanup
  (removing the dead source) is optional and left to the operator. See [[apt-update-best-effort]].

- 2026-07-11: AGENT RUNS AS ROOT (v0.1.10). Found on live hardware: apt/systemctl/reboot jobs failed on
  every node - the hardened, unprivileged systemd unit (`User=servercenter`, `NoNewPrivileges=true`,
  `ProtectSystem=strict`) gave the agent no write access to /var and no way to escalate, which is
  fundamentally incompatible with its job (manage the node). Decided the agent runs as root - the
  standard infra/config-mgmt-agent posture (Ansible et al.) - dropping User/Group + NoNewPrivileges +
  ProtectSystem/ProtectHome/PrivateTmp from Deploy/servercenter-agent.service, and the `servercenter`
  useradd + chowns from install.sh and the updater. Lands on live nodes via auto-update (the updater
  already swaps the unit + daemon-reloads). Targeted hardening (ReadWritePaths/CapabilityBoundingSet)
  is a later refinement, not a blocker. See [[agent-privilege-gap]].

- 2026-07-11: POST-PHASE-7 PRODUCT HARDENING (many slices, driven by real-hardware testing; versions
  0.1.1 -> 0.1.9). Theme: turn the working control plane into a usable product. (1) DEPLOY: turnkey
  `install.sh --with-controller` (node zero in one command) + `--controller` flag for guests; controller
  as GHCR image. (2) AUTO-UPDATE: controller-distributed (agents pull bundles FROM the controller, never
  GitHub; controller bakes the bundles into its image + serves /agent/version + /agent/bundle/{rid};
  systemd timers on each node) - interim, no rollback (Phase 10 is the real blue-green). (3) DIAGNOSTICS:
  real agent version + os/arch + reboot-pending + live CPU/mem/disk with absolute totals (ISystemInfo ->
  LinuxSystemInfo). (4) OPERATOR API+UI: store/list endpoints for every declarative surface (no more
  sqlite3), default `apt` policy seeded. (5) DISCOVERY: service enumeration -> UI picker, libvirt VM
  auto-link by name, policy dropdown. (6) UI redesign: table -> tabs -> CARD grid with per-card actions
  (actions moved onto the card; the select-then-act model was removed as bad UX per user). DECIDED but
  NOT built: pending->approve trust gate ([[trust-onboarding-model]]). Version-bump rule established:
  bump on every shippable change without asking ([[version-bump-rule]]).

- 2026-07-10: CONTROLLER IMAGE UN-DEFERRED (user: the hypervisor is a separate machine and will NOT
  pull source). The compose I first wrote used `build: context: ../..` - wrong, it needs the source
  tree on the hypervisor. Fix: publish the controller as a GHCR image. Added `release-controller.yml`
  (version-gated on VersionPrefix, tag controller-v<version>; docker login-action@v4 / setup-buildx@v4
  / build-push@v7 - verified latest) that builds Deploy/controller/Dockerfile and pushes
  ghcr.io/matthewjenner/servercenter-controller:<version>+:latest. `Deploy/controller/docker-compose.yml`
  now references `image:` (build block commented for local dev) - the hypervisor copies JUST that file
  + a templates/ dir and runs `docker compose pull && up -d` (no source, no SDK). One-time: GHCR
  package -> public (public repo) or `docker login ghcr.io`. This un-defers the image-controller
  publish track. UI runs from source on the workstation for now; AGREED fast-follow post-compact is a
  VELOPACK installer for the UI (release-ui.yml, ui-v<version>) - the model in build-and-update.md.
  See `Docs/dev-environment.md`.

- 2026-07-10: CI/CD FIXED + STYLE ENFORCED + RELEASE VERIFIED (user's 3 asks). (1) CI + Release Agent
  were failing on `LibvirtStatePollerTests.Seeds_the_cache_from_the_libvirt_domain_list` - a RACE, not
  transient: it assumed `BackgroundService.StartAsync` runs `ExecuteAsync` to completion, but it runs
  in the background, so on Linux the cache was empty when asserted. Fixed by waiting for the seed with
  a timeout (see [[lessons-and-gotchas]]). (2) NEW HOUSE RULE (user): NO var, NO top-level statements -
  explicit types everywhere, full classes, file-scoped namespaces. Program.cs converted to
  `public class Program { static async Task Main }` in namespace ServerCenter.Controller (the 8
  integration tests using WebApplicationFactory<Program> got `using ServerCenter.Controller;`; WAF
  works with the namespaced explicit-Main Program). `.editorconfig` sets csharp_style_var_* = false +
  IDE0007/0008 = warning; Directory.Build.props adds EnforceCodeStyleInBuild, so under
  TreatWarningsAsErrors a stray `var` now FAILS the build. `dotnet format style` (needed a couple of
  passes) converted all ~280 var -> explicit; 0 remain. See [[code-style-explicit]]. (3) Release flow
  VERIFIED sound: release-agent.yml (version-gated on VersionPrefix, tag agent-v<v>) builds
  self-contained SINGLE-FILE tarballs via publish-agent.sh (binary + systemd unit + install.sh - NOT
  raw bin/) and creates a GitHub release; install.sh does a real systemd install (dedicated user, no
  runtime needed) on host + guests. It was blocked ONLY by the poller test; no agent release exists
  yet, so the next push publishes agent-v0.1.0. All 249 tests green under the enforced build.

- 2026-07-10: ROADMAP REPRIORITIZATION (user). Goal before any new feature phase: a STABLE LINUX
  platform working END TO END. Consequences: (1) ALL Windows work is deferred indefinitely - Phase 8
  (Windows agent), Phase 9 (Windows updates), the WindowsServiceController stub - because Windows VMs
  are rare (a game or two). (2) The S3 IObjectStore + the save-backup JOB are deferred to ~1.0. So
  near-term effort is the Linux real-hardware smokes (Tier 2/3 on the actual hypervisor) that only the
  user can run, plus any packaging/wiring they need (e.g. the controller container image + libvirt
  socket mount, node-zero install from the GitHub release) - NOT more feature phases. Do not start
  Phase 8/9 or the S3 work without an explicit ask.

- 2026-07-10: Phase 7d - the provisioning -> managed handoff closes Phase 7 (at the Tier-1/handoff
  level). A node is recorded 'provisioning' with its libvirt_domain BEFORE its agent exists (agent_id
  null), and flips to 'managed' adopting its agent on first check-in. The flip is a SEPARATE explicit
  step (MarkManagedOnCheckIn), NOT EnsureNode, because EnsureNode is ON CONFLICT DO NOTHING - it must
  not clobber a provisioning row's domain/lifecycle; so connect does EnsureAgent -> EnsureNode
  (no-op for the pre-provisioned node) -> the flip. Ordering matters: agent_id has an FK to
  agent_identity, so the flip must run AFTER EnsureAgent has created the identity row (it does).
  Preserving libvirt_domain through the handoff is the point - it immediately makes VM-running truth
  (6b) and VM-lifecycle jobs (6c) work for the newly-managed node, closing the Phase 6 loop. The real
  VM bring-up (cloud-init first boot, libvirt DEFINE from XML + start) is Tier-3 and needs an
  ILibvirtHost.Define the seam does not have yet - explicitly deferred to real-hardware work. Tier 1
  + a real-gRPC handoff integration test (+4).

- 2026-07-10: Phase 7c - the recipe.apply engine, which lands "late and cheap" exactly as the brief
  predicted: it is almost entirely COMPOSITION of primitives already built (package install, SteamCMD,
  ConfigGen, the 7b script runner, service control) in a fixed convergent order. Only two small new
  pieces were needed: IPackageInstaller (INSTALL what a build needs - distinct from IUpdateProvider,
  which UPDATES what is installed; apt-get install is idempotent so it is convergent) and
  IServiceController.ReloadAsync (systemd daemon-reload so a freshly-written unit is picked up; no-op
  on Windows SCM). The systemd unit is rendered by a pure SystemdUnitRenderer and written via the
  SAME IConfigWriter as game configs - a unit file is just another file. Recipe convergence is
  inherited from its parts (each step idempotent), so the executor itself has no special-case repair
  logic - build = repair = rebuild falls out. recipe.apply is requeueable (convergent). Templates ship
  inline with the job, same mechanism as server.config-apply. Tier 1 + a real-gRPC end-to-end test
  (+9). The recipe ENGINE DoD is met; the remaining Phase 7 DoD (cloud-init first boot + libvirt
  define/start of a base image + the provisioning->managed handoff) is 7d + Tier-3 real-VM work.

- 2026-07-10: Phase 7b - the idempotent script runner, the ONE genuinely new primitive (everything
  else in Phase 7 reuses existing primitives). Convergence is data-driven, not code: each step's
  alreadyDone check gates whether it runs, onSuccess marks it done - the runner has no per-step
  special-casing, it just honors the recipe data. This is what realizes build = repair = rebuild.
  Commands run via `sh -c` (they are shell expressions - test -f, touch, pipes). A failed step stops
  the run (later steps may depend on earlier ones) and reports FailedScriptId. Lives in Agent.Linux
  over IProcessRunner (same shell-exec home as apt/steamcmd), Tier-1 tested by exit-code-per-command.

- 2026-07-10: Phase 7a - the BuildRecipe declarative surface (the third and last class catalog).
  Deliberately REUSES the descriptor's SteamAppSpec + ConfigFileSpec rather than defining recipe-
  specific twins - recipe and descriptor speak the same primitive vocabulary (a recipe's steamApp IS
  a SteamCMD app; its configFiles ARE config-template files), so they share types and the game
  dialect (GameDescriptorSerializer.Options). Convergence is baked into the DATA: RecipeScript carries
  alreadyDone (skip-if) + onSuccess (mark-done), so the engine (7b/7c) is convergent by construction,
  not by special-case code - build = repair = rebuild. Schema V5 = the last of the three class tables
  (update_policy/game_descriptor/build_recipe); server_instance already pins recipe_id/version (V4).
  Tier 1 only (+5).

- 2026-07-10: Phase 6c - VM lifecycle as CONTROLLER-driven jobs closes Phase 6. Unlike every prior
  job (controller -> agent), these execute ON the controller against local libvirt (brief 3.2/3.3:
  the host control plane is strictly controller-driven; the two planes never conflate). Still a REAL
  persisted job - visible in JobView, in history - just with a controller-local execution path
  instead of the agent transport. Executed INLINE (not fire-and-forget): the libvirt verbs
  (start/shutdown/reboot) only REQUEST a transition and return fast; the domain changes async and the
  6b poller reflects it - so inline keeps the job state deterministic + Tier-1 testable without
  WaitUntil. The trigger is keyed by NODE, not agent (VM lifecycle targets a node's domain; the
  controller resolves it) - a new JobView.TriggerVmAction gRPC with a typed VmLifecycleAction enum;
  the dashboard uses one parameterized RelayCommand with three buttons. A libvirt failure persists a
  FAILED job (dispatch still returns Dispatched - the job record carries the outcome, same as agent
  jobs). Tier 1 (+6). Phase 6 DoD MET; real virsh is the user's Tier-3 hypervisor smoke.

- 2026-07-10: Phase 6b - dual-truth becomes REAL. VM-running truth lives in an in-memory cache
  (LibvirtDomainStates, mirroring AgentPresenceStore - transient live truth, NOT persisted) fed by a
  BackgroundService poller, so the fleet snapshot build stays fast (reads the cache, does not shell
  virsh on the 2s hot path). Dual-truth is two INDEPENDENT facts that can disagree; the mapping keeps
  Unknown first-class (no linked domain, or a domain libvirt hasn't reported yet -> Unknown, never a
  lying Stopped - brief 3.7). The whole libvirt stack is GATED on Libvirt:Enabled: off (dev/test
  default) registers a NullLibvirtHost (VM state stays Unknown, lifecycle throws loudly so a
  misconfig surfaces) and no poller; on registers VirshLibvirtHost + the poller. node.libvirt_domain
  (already in schema V1) is now read into NodeRow and set via SetLibvirtDomainAsync + a dev endpoint;
  Phase 7 provisioning will set it automatically. Tier 1 (+5, FakeLibvirtHost).

- 2026-07-10: Phase 6a - the ILibvirtHost primitive. libvirt is CONTROLLER-driven (the controller
  container has the socket mounted; the agent is uninvolved - VM-lifecycle vs in-guest are the two
  never-conflated planes, brief 3.2/3.3). Design choice: split the PURE parser (VirshOutputParser:
  virsh list/dominfo text -> DomainInfo/DomainState) from a thin virsh process adapter
  (VirshLibvirtHost), so the fiddly parsing is Tier 1 and the adapter is Tier 3 only (libvirt is
  invisible to containers - no Tier 2, a fidelity boundary). Deliberately did NOT promote
  IProcessRunner (Agent.Linux) to Core to feed the adapter - instead VirshLibvirtHost uses
  System.Diagnostics.Process DIRECTLY, exactly the TcpRconChannel/HttpFetcher leaf-adapter pattern:
  the thing worth testing (parsing) is extracted and pure, the leaf I/O is Tier 3. This avoided a
  ~10-file refactor for zero test-coverage gain. WatchEvents polls ListDomains (a `virsh event`
  stream is the future push upgrade). FakeLibvirtHost (TestFakes) transitions domain state on
  start/shutdown/reboot so the 6b fleet view + 6c jobs are Tier 1 testable. Tier 1 only (+9).

- 2026-07-10: Phase 5f - descriptor-driven server jobs close the Phase 5 DoD end to end. Same
  controller-resolves / agent-executes split as the update plane: ServerJobDispatcher loads the
  instance + its PINNED descriptor version and ships concrete params; the agent never sees the
  descriptor. Config templates are shipped INLINE with the config-apply job (controller reads them
  and puts the text in the params) so rendering stays decentralized on the agent while the controller
  owns the template data - the interim template store is a directory on the controller
  (FileConfigTemplateSource, Templates:Root), a DB-backed store is a later refinement (same path
  descriptors/policies took). server.install + config-apply are convergent (SteamCMD ensure /
  idempotent render) so both are requeueable. SCOPE: server.BACKUP job wiring was deferred - it needs
  a real S3 IObjectStore (only the in-memory fake exists); the SaveBackup capability itself is done +
  Tier-1 proven, so this is wiring + an AWS impl, not new design. Readiness is a status PROBE, not a
  mutating job, so it has no executor (it will feed node status later). Controller gained refs to
  Primitives + Capabilities (InstanceParamsResolver + FileConfigTemplateSource). Proto/domain JobState
  clash hit again in the new integration test - qualified the domain side. Tier 1 + a real-gRPC
  end-to-end install test (+9). Phase 5 DoD MET (descriptor-driven anonymous SteamCMD install + config
  templated from instance params, both as dispatched jobs).

- 2026-07-10: Phase 5e - SaveBackup + Readiness (completing all 5 ICapability impls). SaveBackup key
  layout: a STABLE instance-scoped key (saves/{instanceId}/saves.zip) rather than timestamped keys -
  the object store is versioned (S3), so each backup is a new version and a "snapshot id" IS an
  object version id; no clock needed, and it mirrors the controller-DB one-backup pattern. Game
  saves are explicitly data-plane on their own path (brief 3.9), separate from the controller's
  precious surface. The archiving (glob enumerate + zip) sits behind IFileSetArchiver so the
  capability's TESTABLE logic - quiesce-runs-before-archive, the S3 key, Put-called - is Tier 1
  against a fake; the real ZipFileSetArchiver (absolute POSIX paths as entry names, restore re-roots
  at "/") is Tier 2. Readiness reuses the config-templating primitive to resolve the port TOKEN
  ("{{ports.game}}") from instance params - no bespoke parsing. Only port-probe is implemented (the
  most universal game-level "is the port listening" signal, which is already NOT process-alive);
  query-protocol/a2s and log-scrape throw NotSupported rather than silently degrading to a weaker
  signal (a2s is the real accepting-players truth; faking it would lie). Added FakeObjectStore
  (in-memory versioned) - the first IObjectStore fake. Tier 1 only (+6).

- 2026-07-10: Phase 5d - the first capabilities (ConfigGen, Stats, Shutdown), those that compose
  primitives ALREADY built (config templating + RCON), split from the file/network-seam ones
  (SaveBackup, Readiness -> 5e) to keep the ship reviewable. Each capability is constructed from its
  descriptor spec + the primitive and implements the same ICapability contract (primitive-backed;
  plugin-backed would be indistinguishable). ConfigGen needed two new seams (IConfigTemplateSource
  resolving schemaRef -> template text, IConfigWriter persisting rendered output) placed in
  Core.Capabilities so the fakes live in TestFakes with no Capabilities dependency; the real File*
  impls are in ServerCenter.Capabilities (Tier 2). Shutdown injects a TimeProvider so the grace wait
  is deterministic (FakeTimeProvider). Shutdown = graceful DRAIN only; the service stop stays the
  executor's job (separation of concerns). RCON endpoint convention lives in one resolver
  (RconEndpoints): host loopback default since the agent runs ON the game node, port ports.rcon,
  password rcon.password, fail loudly on missing. Stats returns RAW command outputs (structured
  player-count parse is game-specific, later). New ServerCenter.Capabilities.Tests project (per-src
  test convention). Tier 1 only (+7).

- 2026-07-10: Phase 5c - the SteamCMD primitive. Placed in Agent.Linux over IProcessRunner (not
  ServerCenter.Primitives), consistent with the apt/Plex "what" providers which also shell out and
  need the runner seam; the ISteamCmd SEAM lives in Core.Primitives so FakeSteamCmd (TestFakes) needs
  no Agent dependency and the capability layer depends only on Core. One convergent operation
  (EnsureApp = ensure-installed = install = repair = update) per the idempotency invariant. Success
  is parsed from steamcmd's "fully installed" marker AND a zero exit (steamcmd's exit codes are
  unreliable alone). Update-detect is buildid-based and split like RCON: the pure KeyValues parse
  (SteamAppManifest.ParseBuildId) is Tier 1, the appmanifest_<id>.acf file read is thin real I/O
  (Tier 2). DEFERRED: querying Steam's latest-available buildid without downloading (true "is an
  update available?") - needs app_info_print parsing; today's compare is installed-buildid pre/post
  an EnsureApp. Tier 1 only (+7).

- 2026-07-10: Phase 5b - the game-server declarative surface. GameDescriptor models capabilities as
  OPTIONAL strongly-typed specs (configGen/saveBackup/stats/shutdown/readiness) each carrying a
  `primitive` name string, rather than a loose dictionary - the shape is known and the per-game
  difference is DATA (paths, commands, ports, the primitive selector), honoring data-over-code. The
  plugin escape hatch (swap "primitive" for "plugin") is NOT modeled yet - deferred until the first
  bespoke plugin actually exists, to avoid a validation seam with nothing behind it. Descriptor JSON
  uses its own dialect (GameDescriptorSerializer: camelCase, lowercase enum tokens like "kv"/"a2s")
  distinct from UpdateJson's kebab, because the brief's descriptor tokens are single lowercase words,
  not kebab. ServerInstance is the instance side: it pins exact descriptor/recipe/policy VERSIONS
  (history reconstructs) and stores instance_params_json opaquely (it holds secrets - precious
  controller state, never on the agent of record, brief 8.4). Schema V4 adds both tables; recipe/
  policy columns exist for shape but are populated by Phases 4/7. InstanceParamsResolver (a pure
  Primitives helper, pairs with ConfigTemplateRenderer) flattens nested params to dotted tokens
  (objects dot, arrays index, scalars stringify) - the concrete class-vs-instance binding. Tier 1
  only (+10).

- 2026-07-10: Phase 5a - the RCON primitive, built first (brief: highest leverage, build early and
  solid) and it back-fills Phase 4's deferred drain/quiesce preflights. Seam choice: a packet-level
  `IRconChannel` (send/receive RconPacket) rather than a raw-socket seam, so the actual client
  LOGIC - the auth handshake (tolerate the pre-auth junk RESPONSE_VALUE, reject on id -1 / mismatch)
  and the multi-packet accumulation (send an empty follow-up packet, accumulate response parts until
  the server echoes it - Valve's documented trick) - is fully Tier 1 testable; the byte framing is a
  thin length-prefixed TCP adapter smoked at Tier 2. RconPacket + type constants + the seam + the
  auth exception live in Core so FakeRconChannel (TestFakes) needs no Primitives reference; the pure
  RconProtocol byte codec + SourceRconClient + TcpRconChannel live in ServerCenter.Primitives. Note
  ExecCommand and AuthResponse share wire type 2 (direction-disambiguated); ids are positive
  monotonic so they never collide with the -1 auth-failure sentinel. Tier 1 only (+7); real RCON
  against a running dedicated server is the Tier 2 smoke.

- 2026-07-10: Phase 4d - the dashboard can drive updates. Added JobView.TriggerUpdate (gRPC),
  symmetric with the existing RestartService trigger: it dispatches through UpdateJobDispatcher as a
  MANUAL trigger (an operator clicking Run update overrides the schedule window and is its own
  confirmation), and returns the dispatch outcome + reason so the UI shows dispatched / not-eligible
  / needs-confirmation rather than silently doing nothing. Reboot-pending display was consciously
  CUT from this slice: doing it honestly needs a NodeState proto field + threading reboot_pending
  through the presence store / FleetSnapshotBuilder + the agent actually detecting reboot-required -
  that is fleet/status + reboot-follow-on work, not a thin UI trigger, so it stays with that future
  slice. Phase 4 COMPLETE. (+3 UI tests; 161 total.)

- 2026-07-10: Phase 4c - update.apply closes the Phase 4 DoD end to end. Split of labor: the
  controller resolves the policy (UpdateJobDispatcher runs the resolver, agent never sees the
  policy) and pushes a concrete `UpdateJobParams` (channel/how/preflight/reboot from the class +
  packages/serviceUnit from the instance = the class-vs-instance split). Key scope decision: the
  executor does NOT perform the reboot - rebooting mid-job kills the agent before the terminal
  CommandResult lands (a resync scenario), and a host reboot is its own special-policy job with
  drain/confirm (brief 3.4); update.apply only RECORDS the ResolveReboot decision, a follow-on job
  acts on it. Preflight is a pluggable `IPreflightAction` set; a policy requiring a step with no
  handler FAILS the job rather than silently skipping (skipping a player-drain before an update
  would be a quiet correctness bug) - only Notify ships now, RCON drain/snapshot/quiesce land with
  their primitives (Phase 5+). `How` stop-update-start/drain-then-update brackets the service via
  IServiceController and always restarts it, even on apply failure (never leave a service down).
  Jobs are cancellable=false + requeueable=false (mid-transaction apt is not cancellable; an
  interrupted update re-checks, it is not blindly requeued - brief 2.1). Shared `UpdateJson` dialect
  for both policy body and job params. Providers wired apt+Plex on Linux, none on Windows (a
  dispatched update.apply fails cleanly there until Phase 9). Tier 1 (executor + dispatcher) + a
  real-gRPC end-to-end integration test (+12). Still open for a full production update: an
  autonomous scheduler to FIRE window-eligible policies (4a left the window as an eligibility gate),
  the reboot follow-on job, and wiring neuter-unattended-upgrades into onboarding.

- 2026-07-10: Phase 4b - the two "what" providers landed, proving IUpdateProvider is a real
  abstraction, not apt-with-extra-steps. apt shells apt-get/apt/dpkg-query behind IProcessRunner
  (same testable pattern as LinuxServiceController); Plex is a genuinely non-apt app channel - it
  reads a downloads manifest over a new `IHttpFetcher` seam, compares the manifest version to the
  installed dpkg version, selects the arch/distro-matched .deb, and applies via download + dpkg -i.
  Key semantic split: apt reboot-required comes from the /var/run/reboot-required flag; a Plex app
  update NEVER reboots (RebootRequired always false). All apt/dpkg runs set DEBIAN_FRONTEND=
  noninteractive - added an env overload to IProcessRunner rather than shell tricks, so it is
  explicit and assertable (systemctl calls use the plain overload). Neuter-unattended-upgrades is
  `systemctl mask --now` of apt-daily{,-upgrade}.timer + unattended-upgrades.service (mask is
  idempotent = convergent). Tests run on Windows / providers on Linux, so Path.Combine yields
  backslashes in tests - assert path RELATIONSHIPS (dpkg installs exactly what was downloaded) not
  literal slashes; and BeEquivalentTo(WithStrictOrdering), not Equal(), for collections-of-
  collections (Equal compares inner lists by reference). Tier 1 only (+13); real apt/Plex is Tier 2.

- 2026-07-10: Phase 4a - the declarative UpdatePolicy surface + brain landed first (contracts
  before providers). Policy is pure data (Core/Updates/UpdatePolicy) stored as versioned JSON keyed
  by (id, version) - immutable revisions, like descriptors/recipes - so a run's exact governing
  policy is always reconstructable. The resolver is pure and shared by controller dispatch and
  tests: DecideStart handles window-eligibility (an operator/manual trigger overrides the window
  AND counts as its own confirmation; a scheduler tick respects window + approval), preflight is
  deduped preserving order; ResolveReboot maps policy x reboot-required to None/Reboot/
  PromptOperator. `when` windows use Cronos 0.13.0 (pure cron eval, no infra): a window is open if a
  cron occurrence fell within the last WindowMinutes; a malformed cron fails CLOSED (never fires
  autonomously). Cron is UTC for now (per-node tz deferred). Enum tokens serialize kebab-case
  ("stop-update-start", "if-required") so the body is hand-authorable and decoupled from C# casing
  (same discipline as the job-state text). NO autonomous scheduler yet - the window is an
  eligibility gate; the background firing service is a later enhancement. Tier 1 only (+23 tests).

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
- 2026-07-10: Dev launcher + scripts policy. `Scripts/dev-stack.sh` (bash) runs the plaintext
  dev stack. House rule reaffirmed: ALL scripts are bash run from Git Bash, never PowerShell
  (recorded in memory). Removed the initial .ps1.
- 2026-07-10: Phase 3c/3d - Linux service control + UI job view (Phase 3 COMPLETE). Linux
  IServiceController shells `systemctl` behind an injectable IProcessRunner (testable on Windows;
  structured property query, not free-text status). JobView is a third operator gRPC service
  (WatchJobs stream + RestartService), following FleetView's pattern; dashboard now has a jobs
  grid + a restart trigger. Recurring proto/domain enum-name clashes (JobState/LogStream/
  AgentLiveness/VmState) resolved by qualifying the proto side or naming VM props ...Text.
  Avalonia 12: Watermark -> PlaceholderText.
- 2026-07-10: Phase 3b - controller job dispatch. ConnectedAgents maps agent id -> live stream
  (KeyValuePair-remove so a stale disconnect cannot evict a reconnect). PersistingSessionSink
  replaced the presence-only sink: it delegates heartbeat/status to AgentPresenceStore AND
  persists job progress/results to SQLite. ApplyProgressAsync flips queued->running + stamps
  started_at, guarded on non-terminal so a late tick cannot revert. Dev trigger is POST
  /jobs/service-restart (operator auth deferred, like FleetView). Node id == agent id in the
  current 1:1 mapping. End-to-end proven with a real-gRPC in-process test + fake IServiceController.
- 2026-07-10: Phase 3a - agent job execution. IAgentCommandHandler.OnCommandAsync now receives
  the transport so a job can stream JobProgress/CommandResult up while running. Execution is
  fire-and-forget from the read loop (does not block the pump); progress carries per-job seq for
  ordering/ack. AgentJobStore replaces EmptyAgentJobStateSource so in-flight jobs are reported on
  resync. Agent now references Agent.Linux + Agent.Windows and picks IServiceController by OS
  (real impls are stubs until 3c/Phase 8).
- 2026-07-10: Phase 2 dashboard. Operator API is a separate gRPC service (FleetView) on the
  controller, not client-cert authenticated (operator auth deferred; UI uses TOFU server-cert
  validation for now). WatchFleet server-streams a snapshot every 2s so the UI reads a stream
  (honors "UI does not poll"). Dual-truth computed controller-side (LivenessTracker) into the
  snapshot; UI just renders. UI on reflection bindings (compiled-binding x:DataType is awkward
  with DataGrid columns). VM proto/UI enum names clashed with domain names - resolved by
  qualifying and by naming the VM props ...Text.
- 2026-07-10: Phase 1.5b - agent publish track enabled early (release-agent.yml) so node zero
  installs from a GitHub release; runs on ubuntu (native linux-x64), version-gated, tag
  namespaced `agent-v<version>` so UI/image tracks can coexist under one product version. Builds
  only agent-relevant projects. UI's Windows-only csproj props ($(OS)-guarded) so ci.yml's Linux
  leg builds the solution cleanly.
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
