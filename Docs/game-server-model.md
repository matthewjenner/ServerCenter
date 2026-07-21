# Game-server model

How a game server is defined, built, run, edited, and removed - and how N servers of the same game
coexist on one VM. This is the clearest worked example of the repo's **data over code** invariant:
every per-game difference is data over a shared primitive, never a new code path. ASCII punctuation
only (house rule).

## The model: class vs instance

Three versioned, controller-owned declarative surfaces:

| Type | Location | What it is |
| ---- | -------- | ---------- |
| `GameDescriptor` | `Src/ServerCenter.Core/Games` | The game **class**: SteamCMD app, config file specs (ConfigGen), RCON, readiness checks. |
| `BuildRecipe` | `Src/ServerCenter.Core/Recipes` | How to stand one up: packages, SteamCMD, config, scripts, and a systemd `ServiceDefinition` (Unit / ExecStart / User / Restart). |
| `ServerInstance` | `Src/ServerCenter.Core/Games` | A **concrete** server bound to a `NodeId`, pinning descriptor / recipe / policy versions plus an opaque `InstanceParamsJson` (ports, passwords, slots). |

The descriptor, recipe, and policy are the reusable **class**; hostname, ports, and passwords are
**instance params**. Multiple instances per node have always been legal at the schema level (surrogate
`id` primary key, node-scoped index, no per-node uniqueness constraint).

## Per-instance scoping: the linchpin

Two instances of one game used to collide, because `SteamAppSpec.InstallDir`,
`ServiceDefinition.Unit` / `ExecStart`, and every `ConfigFileSpec.Path` were **class-level
constants** - both instances resolved to the same directory and the same systemd unit.

The fix is data, not code: those strings are rendered through the existing `ConfigTemplateRenderer`
(`{{token}}`) against a **reserved token namespace** built by `InstanceContext`
(`Src/ServerCenter.Primitives/ConfigTemplating`):

| Token | Meaning |
| ----- | ------- |
| `{{instance.id}}` | The `ServerInstance` id - the uniqueness and scoping key. |
| `{{instance.name}}` | Display name. |
| `{{instance.dir}}` | The rendered install directory, so ExecStart and config paths can reference it. |
| `{{node.id}}` | The node the instance is bound to. |

Rendering happens **controller-side** in `ServerJobDispatcher`, before the job params are packed, for
`server.install`, `server.config-apply`, and `recipe.apply`. The agent stays dumb - it receives
concrete, per-instance strings and needs no knowledge of instances.

So a descriptor/recipe is authored **once** with, for example, install dir
`/opt/servercenter/<game>/{{instance.id}}` and unit `sc-<game>-{{instance.id}}.service`, and every
instance automatically gets its own directory and unit. A missing token raises
`KeyNotFoundException`, which the dispatcher surfaces as a `NotConfigured` outcome rather than
silently rendering an empty path.

**Conventions:** units are `sc-<game>-<instanceId>.service`; installs live in
`/opt/servercenter/<game>/<instanceId>`.

## Jobs

Everything mutating is a persisted job (the repo-wide invariant). The game-server job types:

| Job type | What it does |
| -------- | ------------ |
| `server.install` | SteamCMD fetch/refresh into the instance's install dir. |
| `server.config-apply` | Render the descriptor's config templates with this instance's params. |
| `recipe.apply` | Convergent build/repair from the recipe (packages, SteamCMD, config, systemd unit). |
| `server.remove` | Teardown: stop + disable + delete the unit, `daemon-reload`, then delete the install dir and config files. |
| `server.config-read` | Read one config file and emit its exact contents as a **single stdout log line**. |
| `server.config-write` | Write raw content back to one config file, verbatim. |

Supporting seams: `IPathCleaner` / `FilePathCleaner` deletes a file or directory tree and is a no-op
when absent (idempotent); `IConfigReader` / `FileConfigReader` reads. The agent runs as root, so the
deletes and writes have permission (see `Docs/identity.md`).

`ConfigReadAsync` / `ConfigWriteAsync` are **path-guarded**: they refuse any path that is not among
the instance's own rendered config files, so the endpoints cannot be used to read or write arbitrary
files on a node. They share a `ResolveFootprint` helper with `RemoveAsync`.

### Raw edit vs template apply

`server.config-write` edits the file on disk directly; `server.config-apply` re-renders the
descriptor template over that same file. They are two doors to one file, and a later config-apply
**will overwrite** a raw edit. This is surfaced in the UI copy and deliberately **not**
auto-reconciled.

## Endpoints

| Endpoint | Purpose |
| -------- | ------- |
| `POST /server-instances` | Store an instance. |
| `GET /server-instances` | All defined instances. |
| `GET /nodes/{nodeId}/server-instances` | One node's instances. |
| `GET /server-instances/{id}/config-files` | The instance's rendered config paths (the editor's file list). |
| `DELETE /server-instances/{id}` | Dispatch the `server.remove` cleanup job, then delete the row. |
| `POST /jobs/server-install`, `/jobs/server-config-apply`, `/jobs/recipe-apply` | Dispatch by agent + instance. |
| `POST /jobs/server-config-read`, `/jobs/server-config-write` | Raw config read/write (node derived from the instance). |
| `GET /jobs/{id}/logs` | A job's persisted log lines - how the editor reads a read-job's stdout back. |

Removal is **delete-after-dispatch**: the cleanup job is dispatched and the row deleted immediately.
If the cleanup later fails, the row is already gone and the failed job shows the orphaned footprint;
the operator can re-create and re-remove.

## Seeded games

`Src/ServerCenter.Controller/Services/DefaultGames.cs` (mirroring `DefaultPolicies.cs`, wired in
`Program.cs`) seeds starter games on startup, idempotently - an id that already exists is left alone,
so an operator edit is never overwritten.

Seeded today: **CS2** (id `cs2`) - appid 730, binary `game/bin/linuxsteamrt64/cs2.sh`, config
`game/csgo/cfg/gameserver.cfg`, port 27015 (+2 per additional instance), GSLT via
`+sv_setsteamaccount`. Ships `templates/cs2/gameserver.cfg`.

Adding another game (Satisfactory, Palworld, ...) should be **pure data** - a descriptor and recipe
authored with the reserved tokens. If a new game seems to need new code, stop: that is the signal to
find the missing primitive instead.

## UI surface

The **Servers tab** is the operator console for this model:

- **Add a server** (modal): pick a game and a node, name it (the name is slugged into the
  filesystem/systemd-safe `instance.id`), and edit prefilled per-game params.
- **Per-instance actions:** Install, Config apply, Recipe apply, Remove, and **Config files...**
- **Config files...** opens the raw config editor (modal): it lists the instance's config paths,
  dispatches a read job and polls `GET /jobs/{id}/logs` for the emitted stdout line, lets the
  operator edit, and writes back with a write job.
- A collapsed **raw JSON** expander stores descriptors/recipes/instances by hand, for power users.

### Planned

Game servers become a **section nested under their host node** rather than a flat tab - the data
model already supports it (`ServerInstance.NodeId`). Per-instance surfaces are shown only when the
descriptor declares that capability (data over code, generic over `GameDescriptor.Capabilities`):
an **RCON console** (descriptor Stats/Shutdown commands; `SourceRconClient` already exists), the
config editor, SteamCMD install/update, and a service restart reusing the same job the Fleet card
dispatches. All the plumbing exists - this is a UI/UX rebuild, not new domain code.
