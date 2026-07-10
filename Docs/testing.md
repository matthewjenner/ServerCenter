# Testing Strategy

Status: draft for review. ASCII punctuation only (house rule).

Global rules govern and are not restated here. Referenced, not repeated: `dotnet test`
must be green before any step is declared done (the build-test-deploy-doc step gate,
`working_style`); assertion library is AwesomeAssertions or FluentAssertions 6.12.x, never
FA >= 7 (paid license, `package_pins`); test-framework and Testcontainers/chaos packages
follow newest-stable (`prefer-latest-packages`). Default framework: xUnit. No conflict with
the brief found; the one tension is that Tier 2/3 cannot run on the per-commit gate (they
need init-capable containers or real VMs), so the "green before done" gate at Tier 1 is the
per-commit line and Tier 2/3 gate at their own cadences (CI / nightly / on-demand).

---

## Core framing

Testing is a three-tier pyramid matched to fidelity ceilings, not a uniform "write tests"
mandate. "Test this" spans subsystems with wildly different testability: the transport/job
core is fully exercisable in-process; game capabilities need real Linux userspace;
VM-lifecycle and Windows Update are invisible below a real VM. Containers are a strong
layer, not the whole strategy. Rule: match each subsystem to the LOWEST tier that still
gives real fidelity, and push risk down as far as it will honestly go.

Risk concentrates in the transport / job / resync core and in idempotent recipes; both sit
fully at Tier 1/2. Windows Update and VM-lifecycle are lower-frequency but only validate at
Tier 3, so the thin Tier-3 layer is planned alongside the container tier, not discovered as
a gap at Phase 6.

---

## Tier 1 - Process/unit + in-memory integration (no containers, per-commit)

The bulk of the suite and the per-commit gate. Drives the whole system in-process with
fakes behind the existing seams (`ILibvirtHost`, `IServiceController`, `IUpdateProvider`,
package/"what" providers, RCON client, S3 client, and the transport - see constraints
below). Zero real infra.

Covers the highest-risk logic: the gRPC bidi contract, job state machine, resync-across-
disconnect (the top refactor trap - test it hardest here), recipe planning and convergence,
libvirt XML/command generation, update-policy resolution, dual-truth state reconciliation.

Mandatory scenarios (these are the ones that bite):
- Kill the stream mid-job -> reconnect -> assert resync reconciles (each rule in contracts
  2.3: still_running replay from last_acked_seq, finished-while-gone, unknown-after-rebuild).
- Controller restart with a job in flight -> assert recovery from SQLite + resync.
- Version-skew the envelope (major mismatch) -> assert clean typed rejection, no payload
  interpretation.
- Apply a recipe twice, and against a half-built target -> assert convergence, no
  double-install, no pristine-only failure.

## Tier 2 - Docker/Podman integration, Linux (CI, not necessarily per-commit)

Real Linux agent against real systemd/DBus, apt, anonymous SteamCMD, RCON against a real
running dedicated-server process, real config templating, real file-set backup. Requires a
systemd-capable container (proper init; Podman handles this more gracefully than Docker).
Testcontainers for .NET orchestrates lifecycle.

Adds:
- Network-chaos tests (toxiproxy / pumba / tc): drop, delay, partition the stream and
  assert reconnect / resync / dual-truth behave. This is where the chaos layer from
  constraint 2 earns its keep.
- Idempotency tests: reset containers to deliberately-dirtied states and assert recipes
  repair / converge (build = repair = rebuild).

High fidelity for the Linux guest-management and game-capability layers.

## Tier 3 - Real VM end-to-end (on-demand / nightly)

The part containers cannot reach. Nested virtualization (KVM-in-a-VM or a nested-virt cloud
instance) for a disposable libvirt sandbox, or a spare/tagged box. Kept deliberately thin.

Covers the genuinely VM-shaped behaviors:
- Provision VM from base image -> cloud-init installs the agent -> provisioning->managed
  handoff.
- Apply a recipe end-to-end against a fresh VM.
- Real reboots: reboot-required detection, apt-upgrade-then-reboot-then-reconnect, the host
  reboot preflight, and the agent self-update watchdog surviving a real restart.
- Windows Update against a real Windows guest; Windows session-0 service/SCM semantics.

---

## Fidelity boundaries (explicit)

These are the lines where a cheaper tier stops telling the truth. Do not cross them.

- Windows containers != Windows VMs. Windows Update essentially does not work in a
  container (no usable WUA, faked reboots, session-0 divergence). Since Update is already
  the most awkward subsystem, its integration test REQUIRES a real Windows VM -
  non-negotiable. Container-test only the Windows agent's interface-conformance (does the
  platform abstraction hold, same shape as Linux); reserve the real Windows VM for Update,
  reboots, and session-0 services. This keeps expensive Windows-VM testing narrow, not "run
  everything twice."
- libvirt / VM-lifecycle is invisible to containers by definition. The whole VM-lifecycle
  plane (define/start/stop/restart, provisioning handoff, cloud-init) cannot be
  container-tested. Unit-test XML/command generation against a fake `ILibvirtHost` (Tier 1);
  validate the real thing only at Tier 3. Do not rely on libvirt-in-a-container for
  fidelity.
- Real reboots and boot-lifecycle (cloud-init first boot, watchdog-across-restart, host
  reboot) are Tier 3 only. Containers fake them poorly.

---

## Two testability constraints designed in now (not retrofitted)

These are code-shape requirements for Phase 0/1, not later test wiring. Both are painful to
add after the fact.

1. Every external-effect boundary is an interface that ships WITH a test-fake / in-memory
   implementation: `ILibvirtHost`, `IServiceController`, `IUpdateProvider`, package/"what"
   providers, RCON client, S3 client. The seams already exist for architectural reasons
   (contracts sections 3, 7, 8); the added requirement is that each has a maintained fake so
   Tier 1 drives the full system with zero real infra. Single highest-leverage testability
   decision. Definition of done for any seam includes its fake.

2. The network transport is injectable, so tests wrap it with a chaos layer
   (drop / delay / partition) without touching production code. Resync correctness depends
   on this being testable at Tier 1 (deterministic in-memory chaos) and Tier 2 (real
   toxiproxy/tc). The `Connect` bidi stream sits behind a transport abstraction from message
   one; production uses the real gRPC channel, tests inject a controllable one.

---

## Per-phase tier coverage

Each phase's definition of done in `workplan.md` names its tier(s). Summary map:

| Phase | Tier 1 | Tier 2 | Tier 3 | Notes |
| ----- | :----: | :----: | :----: | ----- |
| 0 Contracts        | -  | -  | -  | Decides constraint 1+2: every seam ships a fake; transport injectable. No runtime tests yet. |
| 1 Stream + jobs    | XX | x  | -  | Resync/envelope/job-SM hammered at Tier 1; one Tier-2 smoke of a real Linux agent stream. |
| 1.5 Node zero      | x  | XX | -  | Real host agent over real systemd. Host-reboot itself is Tier 3 (Phase 9-ish drill). |
| 2 Dashboard        | XX | -  | -  | Dual-truth reconciliation as view-model tests with fake state feeds. |
| 3 First jobs       | XX | x  | -  | Job lifecycle incl. mid-run disconnect at Tier 1; real systemd service restart at Tier 2. |
| 4 Updates (policy) | XX | XX | -  | Policy resolution at Tier 1; real apt AND Plex + network chaos at Tier 2. |
| 5 Game capability  | XX | XX | -  | Templating/descriptor/RCON-logic at Tier 1; real SteamCMD/RCON/backup/readiness at Tier 2. |
| 6 libvirt lifecycle| XX | -  | XX | XML/command gen vs fake ILibvirtHost at Tier 1; real define/start/stop ONLY at Tier 3 (no Tier 2). |
| 7 Provisioning     | XX | XX | XX | Recipe planning/convergence at Tier 1; dirtied-container idempotency at Tier 2; cloud-init + handoff at Tier 3. |
| 8 Windows agent    | XX | x  | x  | Interface-conformance at Tier 1/2 (shape holds); session-0 SCM semantics at Tier 3. |
| 9 Windows Update   | -  | -  | XX | Real Windows VM only. Non-negotiable per fidelity boundary. |
| 10 Self-update     | XX | -  | XX | Blue-green flip / rollback decision at Tier 1; watchdog surviving a real restart at Tier 3. |

XX = primary coverage, x = secondary/smoke, - = not applicable at that tier.
