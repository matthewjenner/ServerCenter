# ServerCenter Workplan

Living build tracker. Functions as a todo list plus micro-plan. Keep it current: at each
phase boundary, check off done items, refresh the Current State block, and append to the
Decisions Log / Known Edges before starting the next phase (house rule:
`maintain-workplan`). ASCII punctuation only (house rule: `avoid-ai-artifacts`).

---

## Current State

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
- Build status: `dotnet build ServerCenter.slnx` clean (0 warnings, TreatWarningsAsErrors
  on); `dotnet test` green (99 Core + 41 Agent + 58 Controller + 8 integration + 8 UI +
  13 Capabilities = 227).
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
