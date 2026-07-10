# Build, Versioning, and Update Distribution

Status: draft for review. ASCII punctuation only (house rule).

ServerCenter is not one shippable like the other Avalonia apps (Klakr / FleaTrackr /
PlexTool). It has three deployables with three genuinely different build and update models.
The versioning scheme and scripts mirror those apps; the distribution splits three ways.

---

## 1. Version source of truth

Single, repo-wide `<VersionPrefix>` in `Directory.Build.props`, inherited by every csproj.
Bump with `Scripts/bump-version.sh` (Major|Minor|Patch). CI reads it and gates releases on
it (a release is cut only when the tag for that version does not yet exist), so there is no
pipeline-side commit. Same mechanism as the other apps.

`IncludeSourceRevisionInInformationalVersion=false` so in-process version reads are clean
(no `+<git-hash>` suffix).

Product version is LOCKSTEP across controller / agent / UI: one number for the whole fleet
manager. This is deliberately decoupled from the WIRE compatibility version, which lives in
the protobuf envelope (`protocol_major` / `protocol_minor`, phase-0-contracts.md 1.1). A
patch release that does not touch the wire leaves the protocol version alone; the two move
independently. Do not conflate "which build" with "can these two talk."

Scaffold starts at `0.1.0` (pre-1.0; nothing is functional yet). It reaches `1.0.0` when the
Phase 2 dashboard + Phase 3 first jobs make it genuinely usable.

## 2. Three deployables, three update models

### 2.1 UI (`ServerCenter.Ui`) - Velopack, exactly like the other apps

Desktop operator app. Velopack for in-app update from GitHub releases: `VelopackApp.Build()
.Run()` is the first call in `Main` (no-op in dev / `dotnet run`); a Phase 2 `UpdateService`
over `Velopack.Sources` checks the GitHub feed and offers install-and-restart. Published
`win-x64` self-contained, packed with `vpk`, release assets attached to the GitHub release.
This is a straight copy of the Klakr / PlexTool release track.

### 2.2 Agent (`ServerCenter.Agent`) - blue-green self-update + watchdog, NOT Velopack

The agent is a systemd / Windows service, not a desktop app, and it already has a bespoke
update design (brief 3.14, Phase 10): two slots, flag/symlink flip, the new binary must
phone home within N seconds or the watchdog rolls back to the previous slot. SSH is
break-glass. Velopack does not fit (no desktop shell, and we need the watchdog/rollback
guarantee).

- Build: multi-RID self-contained publish - `linux-x64` first (Ubuntu before Windows,
  brief 3.13), `win-x64` later, `linux-arm64` if a node needs it. Each RID is one bundle.
- Distribution: controller-mediated. The controller holds the agent bundles as precious-ish
  build artifacts (they are rebuildable, so not backed up as precious, brief 3.9); the
  agent self-update JOB (Phase 10) pulls the target bundle from the controller, stages it in
  the idle slot, flips, and proves phone-home. CI produces the per-RID bundles and attaches
  them to the GitHub release; the operator (or an automated step) ingests them into the
  controller. Agents never pull from GitHub directly - only trust and distribution are
  centralized (brief 3.8).

### 2.3 Controller (`ServerCenter.Controller`) - container image, NOT Velopack

Containerized on the hypervisor host with the libvirt socket mounted (brief 3.3). Its update
is a new image: pull, recreate the container. CI builds the image and pushes it to a registry
(proposed: GHCR, `ghcr.io/matthewjenner/servercenter-controller`, public repo). A controller
update briefly drops the control plane; agents' jobs outlive the connection and resync on
reconnect (phase-0-contracts.md 2.3), and the UI expects to lose and reconnect - the same
posture as the host-reboot case (brief 3.4). Recommend a controller-DB snapshot (backup
runbook) immediately before recreate.

## 3. CI mapping

| Workflow            | Trigger                     | Does                                                       | Publishes |
| ------------------- | --------------------------- | --------------------------------------------------------- | --------- |
| `ci.yml`            | push + PR to main           | restore / build / test the whole solution, matrix Linux+Windows | nothing (safe) |
| `release-ui.yml`    | push to main, version-gated | publish win-x64, `vpk pack`, GitHub release assets        | UI Velopack release |
| `release-agent.yml` | push to main, version-gated | multi-RID self-contained publish, attach bundles          | agent bundles |
| `image-controller.yml` | push to main, version-gated | docker build + push                                    | GHCR image |

`ci.yml` exists now (non-publishing, safe on a non-functional scaffold). Same version-gate
idempotency as the other apps' `release.yml` will apply to the publish tracks.

Decision (2026-07-10): publishing was initially deferred until ~1.0.0. UPDATED same day - the
AGENT track is enabled now (`release-agent.yml`), because deploying node zero (Phase 1.5) needs
the agent installable from a GitHub release. The UI (`release-ui.yml`) and controller image
(`image-controller.yml`) tracks remain deferred until they are worth shipping. Registry for the
controller image is confirmed GHCR (`ghcr.io/matthewjenner/servercenter-controller`,
GITHUB_TOKEN auth) when that track lands.

`release-agent.yml` specifics: runs on ubuntu-latest (native linux-x64), version-gated and
idempotent (tag `agent-v<version>`; namespaced so UI/image tracks can coexist), builds/tests
only agent-relevant projects (not the Windows UI), packages linux-x64 + linux-arm64 tarballs
via `Scripts/publish-agent.sh`, and attaches them to the release. Actions pinned to verified
latest (`actions/checkout@v7`, `actions/setup-dotnet@v5`). Tags are namespaced per track, so
the product `VersionPrefix` still drives all of them but they release independently.

## 4. GitHub Actions versions

Per the global rule, action majors are verified against the live latest, not training data
(the Node 24 runner change retired the `@v4` pins the other repos still use). Current pins:
`actions/checkout@v7`, `actions/setup-dotnet@v5`, and for the controller image
`docker/login-action@v4`, `docker/metadata-action@v6`, `docker/build-push-action@v7`.
Re-verify on any workflow edit.
