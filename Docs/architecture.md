# Architecture Overview

Status: draft for review (Phase 0). This is a durable design doc generated from the
planning brief in `CLAUDE.md`. Where this doc and the house rules disagree on style,
testing, or structure, the house rules win.

Punctuation note: this repo uses plain ASCII punctuation everywhere (no em-dashes,
en-dashes, unicode ellipsis, or curly quotes), per the `avoid-ai-artifacts` house rule.

---

## 1. The three tiers

```
          +-----------------------------------------------------------+
          |                        UI (Avalonia)                      |
          |   thin view onto the controller; talks to nothing else    |
          +----------------------------+------------------------------+
                                       | gRPC (operator-facing API)
                                       v
          +-----------------------------------------------------------+
          |                        Controller (.NET)                  |
          |  source of truth + root of trust                          |
          |  - SQLite (all precious state) -> S3 (one backup surface) |
          |  - job engine (every mutation is a persisted job)         |
          |  - libvirt LOCAL via mounted socket (qemu:///system)      |
          |  - S3 mediation (backups, save files)                     |
          |  containerized ON the hypervisor host, socket punched in  |
          +----+---------------------------------------------+--------+
               ^                                              |
               | gRPC bidi stream                             | local unix socket
               | (agent dials OUT; controller pushes DOWN)    v
     +---------+----------+                           +-----------------+
     |   Agent (.NET)     |  ... one per node ...     |  libvirt / KVM  |
     | systemd / Win svc  |                           |  (domains)      |
     | per-OS actions     |                           +-----------------+
     +--------------------+
```

Every managed node runs one agent, including the hypervisor host itself (node zero,
see below). The agent dials out to the controller and holds the stream open. Stream up
means the agent is online; stream dropped means it is offline. There are no inbound
ports on guests.

## 2. Two control planes, never conflated

The system has two distinct planes that hit different code paths and different
endpoints. This separation is load-bearing (brief 3.2).

| Plane          | Owner            | Reaches                     | Examples                                    |
| -------------- | ---------------- | --------------------------- | ------------------------------------------- |
| In-guest       | Agent            | the OS inside a node        | OS updates, service restart, process health |
| VM-lifecycle   | Controller       | libvirt domains (local)     | define / start / stop / restart, snapshots  |

"Restart Plex" (in-guest, agent) and "restart the VM" (VM-lifecycle, controller ->
libvirt) are different operations. Guests are unaware they are virtualized and never
call back to the hypervisor. The host control plane is strictly controller-driven. We
knowingly forgo guest -> host callback features.

## 3. Node zero: the host is a normal managed node

The controller container cannot manage its own host, and we do not make it privileged.
Instead the same agent runs natively on the host via systemd. Host updates, health, and
reboot-required flow through the identical agent interfaces used by every guest. Host
management is a free consequence, not a special subsystem.

The single exception is policy, not mechanism: a host reboot takes down every guest and
the controller container itself. It is mechanically the same update job, but it needs a
special preflight (warn / drain guests, require confirmation) and the UI must expect to
lose the controller and simply reconnect when the host returns. The controller cannot
narrate its own host's reboot; the UI owns that reconnect expectation.

## 4. Dual-truth state (no single lying green dot)

Two independent facts are tracked and shown separately, never collapsed into one status:

- Agent-online: derived from the gRPC stream lifecycle (in-guest plane).
- VM-running: derived from the libvirt event stream (VM-lifecycle plane).

They can legitimately disagree (agent dead but VM up; VM up but OS hung). `Stale` and
`Unknown` are first-class states, not errors. Example surface: "VM: running (libvirt) /
Agent: no contact 6m."

## 5. The declarative spine

The same shape appears three times. It is the backbone that stops the system sprawling
into per-game / per-service / per-server special cases. Build a small primitive library
once; express all variation as controller-owned, versioned data.

```
   Three declarative surfaces (data)          Shared primitive library (code)
   --------------------------------           -------------------------------
   Game capability / descriptor  ---selects-->  Config templating
   Update policy                 ---selects-->  SteamCMD (anonymous)
   Build recipe                  ---composes->  RCON
                                                File-set backup (+ quiesce)
                                                Service control (systemd / SCM)
   Plugin escape hatch                          Package / "what" provider (apt, Plex)
   (implements the SAME                         Readiness (log-scrape/port-probe/query)
    capability contract as a                    Idempotent ordered script runner
    primitive; callers cannot tell)
```

Class vs instance split applies to all three surfaces: the recipe / descriptor / policy
is the reusable versioned class; a specific server's hostname, ports, RCON password,
slots, and map rotation are instance params fed in at build or run time. One recipe maps
to many servers differing only in data.

Design pressure: every per-game / per-service / per-server difference should be data over
a shared primitive, not new code. Anywhere the plan risks a special case, the fix is to
name the primitive that absorbs it (see `phase-0-contracts.md`, primitive library).

## 6. The job model is the spine of all mutation

Every mutating operation is a persisted job (restart service, restart VM, run updates,
apply a recipe, provision). No exceptions. Jobs live on the controller, survive
controller restart, and outlive their connection: an `apt upgrade` keeps running when the
stream drops, and the agent resyncs job state on reconnect. Details, state machine, and
schema in `phase-0-contracts.md`.

## 7. The one-backup invariant

All precious state lives on the controller. Agents and VMs are disposable. The
controller's SQLite (shipped to S3) is the single control-plane backup surface. Wanting
to back up an agent is a smell: precious state leaked out of the controller and should be
moved back. Data-plane bulk (game save files, base OS image) is backed up by purpose on
its own path, not lumped in. Mechanics and restore test in `backup-restore-runbook.md`.

## 8. What agents may and may not hold

Agents are disposable and hold only transient state: in-flight job state for resync,
which is cheap to lose (at most a re-query or re-run). Agents hold no identities of record
(controller-owned), no templates or images (none exist), and no descriptors / policies /
recipes of record (pulled from the controller). Rebuild an agent, it re-registers, pulls
what it needs, and is whole again.

## 9. Repository shape (proposed)

A single .NET solution, contracts-first:

Folder names use Title Case to match dotnet conventions.

```
ServerCenter.slnx     # XML solution format (current dotnet standard)
  Contracts/            # .proto files + generated code target; the versioned wire
  Src/
    ServerCenter.Controller/       # backend, job engine, libvirt, S3, SQLite
    ServerCenter.Agent/            # cross-platform agent host
    ServerCenter.Agent.Linux/      # systemd / DBus / apt implementations
    ServerCenter.Agent.Windows/    # SCM / WUA implementations
    ServerCenter.Primitives/       # the shared primitive library
    ServerCenter.Capabilities/     # ICapability + descriptor-driven capabilities
    ServerCenter.Ui/               # Avalonia
    ServerCenter.Core/             # shared domain types, job model, envelope helpers
  Tests/                           # mirrors Src/, per house testing rules
  Docs/                            # these design docs + workplan.md
```

Platform-divergent code lives behind interfaces in `ServerCenter.Core` /
`ServerCenter.Agent` with per-OS implementations in `.Linux` / `.Windows`. Ubuntu is
implemented first against those interfaces; Windows is implemented later against the same,
now-proven, interfaces (brief 3.13).
