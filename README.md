# ServerCenter

A self-hosted **.NET 10 control plane for a home virtualization rack** - one place to manage a
KVM/libvirt hypervisor and the VMs it runs (SteamCMD game servers, Plex, storage, web). You declare
what each node should be; ServerCenter converges it and keeps a durable, backed-up record of the
truth.

> Status: early and Linux-first. Phases 0-7 are complete (control plane, jobs, identity/mTLS,
> declarative update policies + game descriptors + build recipes, VM lifecycle). Windows agent
> support and S3 backup are intentionally deferred. Version `0.1.14`.

## What it is

Three tiers, one job-driven spine:

- **Agent** - a native per-node .NET service. The *same* binary runs on every managed VM and on the
  hypervisor host itself ("node zero"). Does the work: package/service control, SteamCMD, config
  templating, RCON, readiness probes - all idempotent.
- **Controller** - containerized on the hypervisor host. The **source of truth and root of trust**:
  owns identity (acts as a CA), stores all precious state in SQLite, drives the local libvirt for
  VM lifecycle, and dispatches jobs to agents. This is the only thing you back up.
- **UI** - a thin Avalonia desktop app that talks *only* to the controller.

Transport is gRPC (bidirectional streaming); persistence is SQLite; the hypervisor is libvirt/KVM.

### Design invariants

- **One backup.** All precious state lives on the controller (SQLite -> versioned S3). Agents and
  VMs are disposable; wanting to back one up is a smell.
- **Everything mutating is a persisted job** that survives controller restart *and* stream
  disconnect (agents resync on reconnect).
- **Data over code.** Every per-game / per-service difference is data over a shared primitive
  library, not new code.
- **Dual-truth state.** "Agent online" (stream) and "VM running" (libvirt) are independent facts
  that can disagree - both are shown; `Stale`/`Unknown` is first-class.

The full reasoning lives in [Docs/architecture.md](Docs/architecture.md).

## Repository layout

| Path | What |
| --- | --- |
| `Src/ServerCenter.Core` | Contracts, domain model, the declarative surfaces (update policies, game descriptors, build recipes). |
| `Src/ServerCenter.Agent` | The agent host (bootstrap, identity/enrollment, job execution). |
| `Src/ServerCenter.Agent.Linux` | Linux providers (apt, Plex channel, SteamCMD, systemd, script runner). |
| `Src/ServerCenter.Agent.Windows` | Windows agent (deferred - not built into the Linux platform yet). |
| `Src/ServerCenter.Controller` | The controller: gRPC services, SQLite persistence, libvirt VM lifecycle, job dispatch. |
| `Src/ServerCenter.Primitives` | Shared primitives (Source RCON, port probe, virsh/libvirt host). |
| `Src/ServerCenter.Capabilities` | Composable capabilities (config-gen, RCON stats/shutdown, save backup, readiness). |
| `Src/ServerCenter.Ui` | Avalonia operator UI. |
| `Tests/` | Core / Agent / Controller / Capabilities / Integration / UI test projects. |
| `Deploy/` | Agent install package + the controller container (Dockerfile + compose). |
| `Docs/` | Architecture, contracts, runbooks, phase plans, the living workplan. |

## Getting started

**Deploy onto a hypervisor you already run** (nothing is copied from your workstation - it all comes
from the GitHub release + the public controller image):

```bash
# On the hypervisor (node zero): stand up the controller AND install the host agent in one command.
curl -L -O https://github.com/matthewjenner/ServerCenter/releases/download/agent-v0.1.14/servercenter-agent-0.1.14-linux-x64.tar.gz
mkdir agent && tar -xzf servercenter-agent-0.1.14-linux-x64.tar.gz -C agent && cd agent
sudo ./install.sh --with-controller
```

Then install the agent on each guest with plain `sudo ./install.sh`. Full instructions, including
the mTLS variant, are in **[Deploy/README.md](Deploy/README.md)**.

**Operator UI (Windows):** download and run the Setup from the latest `ui-v*`
[release](https://github.com/matthewjenner/ServerCenter/releases); it self-updates from future
releases (Velopack). Point it at your controller in the Settings tab.

**Build and run from source** (developers): see **[Docs/dev-environment.md](Docs/dev-environment.md)**.

```bash
dotnet build ServerCenter.slnx
dotnet test  ServerCenter.slnx
```

## Documentation

- **[Docs/workplan.md](Docs/workplan.md)** - the living build tracker + decisions log (read this first).
- **[Docs/architecture.md](Docs/architecture.md)** - the three tiers, the spine, the invariants.
- **[Docs/build-and-update.md](Docs/build-and-update.md)** - versioning and the three update models
  (UI Velopack, agent blue-green, controller container image).
- **[Docs/dev-environment.md](Docs/dev-environment.md)** - build/run/deploy, code style, IDE notes.
- **[Docs/linux-smoke-runbook.md](Docs/linux-smoke-runbook.md)** - ordered end-to-end bring-up on
  real hardware.
- **[Docs/backup-restore-runbook.md](Docs/backup-restore-runbook.md)** - consistent snapshot ->
  versioned store, and how to *test the restore*.
- **[Docs/testing.md](Docs/testing.md)** - test tiers and conventions.
- **[Docs/phase-0-contracts.md](Docs/phase-0-contracts.md)** - the contract-first foundation.
- **[Deploy/README.md](Deploy/README.md)** - installing the agent and the controller on a node.

## Contributing / conventions

Code style is build-enforced: no `var`, no top-level statements, explicit types, file-scoped
namespaces (`.editorconfig` + `EnforceCodeStyleInBuild` under `TreatWarningsAsErrors`). `dotnet build
ServerCenter.slnx` is authoritative - the IDE may show phantom errors from stale generated code (see
[Docs/dev-environment.md](Docs/dev-environment.md)). Contracts land before code.
