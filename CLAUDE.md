# CLAUDE.md — Homelab Fleet Manager

> **This file is a router, not a library.** It carries the durable rules that govern _every_ task, plus pointers to the detailed docs. Pull the heavy docs into context only when the task needs them.

## Global rules

**The repo docs are the standard and are self-contained** - anyone (or any agent) can work here from this file plus `/Docs`, with no external context required. `/Docs/conventions.md` governs style, scripts, dependencies, versioning, and the quality bar; `/Docs/testing.md` governs testing. Follow them and cite them rather than restating them. Flag genuine conflicts instead of guessing.

## What this is

A .NET 10 control plane for a home virtualization rack. Three tiers: **Agent** (native per-node .NET service), **Controller** (containerized on the hypervisor host, source of truth + root of trust), **Avalonia UI** (thin, talks only to the controller). gRPC bidi transport, SQLite persistence, S3 backup, libvirt/KVM hypervisor. The user is an experienced architect — spend words on hard parts, not basics.

## Invariants (never violate; design _to_ these)

- **One-backup invariant.** All precious state lives on the controller (identities, descriptors, update policies, build recipes, instance params, job history) in SQLite → versioned S3. **Agents and VMs are disposable.** Wanting to back up an agent = a smell; move the state back to the controller. Save files and base OS images are data-plane, backed up by purpose, not lumped in. **No golden images.**
- **Everything mutating is a persisted job.** No exceptions. Jobs survive controller restart and **survive stream disconnect** — agents keep local job state and **resync on reconnect**. States: `queued → running → (succeeded|failed|timedout|cancelled)`.
- **Two control planes, never conflated.** In-guest (agent: updates, service/game state) vs VM-lifecycle (controller → local libvirt). Host control plane is strictly controller-driven; guests are unaware they're virtualized.
- **Controller owns identity.** Mints/stores/pins per-agent identity; root of trust centralized, _work_ decentralized. Keep it behind an interface (seam for a future untrusted node).
- **Dual-truth state.** Agent-online (stream) and VM-running (libvirt) are independent facts that can disagree. Show both. `Stale`/`Unknown` is a first-class state, never one lying green dot.
- **Data over code.** Every per-game / per-service / per-server difference is **data over a shared primitive**, not new code. If a task risks a special case, stop and propose the primitive that absorbs it.
- **Recipes are idempotent/convergent.** `ensure-*`, not `install/write/start`. build = repair = rebuild.
- **Host = node zero.** Same agent runs natively on the host. Host reboot is a special-policy job (drain/warn guests, require confirmation, expect to lose + reconnect the controller).

## The declarative spine

Three declarative surfaces, all controller-owned versioned data: **game capability descriptors**, **update policies**, **build recipes**. All compose a shared **primitive library**: config-templating, SteamCMD (anonymous), RCON, file-set backup, service-control, package/"what" providers (apt **and** Plex-style app channels), log-scrape/port-probe/query readiness, idempotent script runner. Bespoke cases use a **plugin escape hatch** implementing the same `ICapability` contract. Recipe/descriptor/policy = reusable _class_; hostname/ports/passwords = _instance params_.

## Docs (pull in as needed)

- Living build tracker + Decisions Log (READ FIRST on cold load) → `/Docs/workplan.md`
- Architecture overview → `/Docs/architecture.md`
- Contracts (protobuf envelope, job state machine, SQLite schema, agent interfaces, capability/`ICapability`, `UpdatePolicy`, `BuildRecipe`) → `/Docs/phase-0-contracts.md` (proto files in `/Contracts`)
- **Conventions and standards** (style, scripts, pins, versioning/release rule, ASCII, secrets) → `/Docs/conventions.md`
- Identity & auth model (mTLS, enrollment, why the agent runs as root, planned approve-gate) → `/Docs/identity.md`
- Game-server model (descriptor/recipe/instance, per-instance scoping, jobs) → `/Docs/game-server-model.md`
- Build/versioning + the three update models + in-guest update profiles → `/Docs/build-and-update.md`
- Backup/restore runbook (consistent snapshot → versioned S3; **test the restore**) → `/Docs/backup-restore-runbook.md`
- Test tiers + practical test traps → `/Docs/testing.md`
- Dev environment: build/run/deploy, code style, IDE phantom errors, code-level traps → `/Docs/dev-environment.md`
- Linux end-to-end smoke checklist → `/Docs/linux-smoke-runbook.md`
- Phase plans / definition of done are inside `/Docs/workplan.md` (there is no separate phases dir).

## Standing reminders

- **Contracts before code.** No feature code ahead of an approved contract.
- Ubuntu agent before Windows; read/status before actions; reuse before bespoke.
- **Code style is build-enforced: no `var`, no top-level statements** - explicit types, full classes,
  file-scoped namespaces (`.editorconfig` + `EnforceCodeStyleInBuild`). Target-typed `new()` is fine.
- **Trust `dotnet build ServerCenter.slnx`, not IDE diagnostics.** The VS Code Roslyn server throws
  phantom CS0246/CS1061 (stale `.slnx`/generated-code workspace); the full build is authoritative.
  See `/Docs/dev-environment.md`.
- ALL Windows work (Phases 8/9) and the S3 backup job are DEFERRED until the Linux platform is stable
  end to end (user decision 2026-07-10). Do not start them without an explicit ask.
- Known traps: resync-across-disconnect (top refactor risk), readiness ≠ process-alive, idempotent steps, Windows Update (session-0, slow reboots), host reboot, consistent SQLite snapshot, "what"-provider must include a non-apt case early, libvirt-from-.NET behind a swappable `ILibvirtHost`.
