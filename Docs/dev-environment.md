# Dev environment notes

Practical notes for building, running, and deploying ServerCenter. ASCII punctuation only (house rule).

## Build + test

```bash
dotnet build ServerCenter.slnx      # full build of all 15 projects
dotnet test  ServerCenter.slnx      # 249 tests across 6 test projects
```

`dotnet build ServerCenter.slnx` is the source of truth. It is a full MSBuild that runs every code
generator and resolves every project reference (see the IDE note below).

## Code style (BUILD-ENFORCED)

House rule: **no `var`, no top-level statements** - explicit types everywhere, full classes,
file-scoped namespaces. This is not advisory: `.editorconfig` sets `csharp_style_var_* = false` +
IDE0007/0008 = warning, and `Directory.Build.props` sets `EnforceCodeStyleInBuild`, so under
`TreatWarningsAsErrors` a stray `var` or block namespace **fails the build**. Target-typed `new()` is
fine (the type is on the left of the `=`). `Program` is a full `class Program { static ... Main }`.

To auto-fix `var` in bulk (it may need 2-3 passes to converge):

```bash
dotnet format style ServerCenter.slnx --diagnostics IDE0008 --severity warn
```

## IDE shows CS0246 / CS1061 but `dotnet build` is clean

This happens with this repo. It is a stale/incomplete IDE workspace, NOT a code problem - the proof
is that `dotnet build` runs the SAME Roslyn compiler and reports zero errors, so if the errors were
real the command line would show them too.

Three reasons the VS Code C# / Roslyn language server disagrees with the build:

1. **Build-time code generation it has not run.** Grpc.Tools generates the proto types
   (`AgentMessage`, `AgentLink.AgentLinkClient`, `TriggerVmActionRequest`, ...) and CommunityToolkit.Mvvm
   generates `[ObservableProperty]` properties + `[RelayCommand]` commands. Stale generator output =>
   those types/members read as CS0246 / CS1061.
2. **`.slnx` is a new solution format** whose loader support in the C# extension is still catching up;
   if it fails to load project-to-project references, every cross-project type becomes CS0246.
3. **Post-edit staleness** - after a large `dotnet format` or a namespace move, the in-memory model
   lags disk until it re-indexes.

**Fix:** Command Palette -> ".NET: Restart Language Server" (or "Developer: Reload Window"); make sure
a build has run so `obj/` holds the generated files; update the C# / C# Dev Kit extensions for better
`.slnx` support. If an error survives a fresh language-server restart AND a clean `dotnet build`, only
then treat it as real.

## Run locally (smoke)

```bash
./Scripts/dev-stack.sh              # plaintext controller + agent + dashboard, on one box
./Scripts/run.sh ui|controller|agent
```

## Deployment / packaging status (IMPORTANT - not all three "release")

- **Agent: RELEASED by CI.** `release-agent.yml` publishes a GitHub release `agent-v<version>` on push
  (version-gated on `VersionPrefix`) with self-contained single-file tarballs (linux-x64 + arm64).
  Install on the host + guests: extract, `sudo ./install.sh` (see `Deploy/README.md`). No agent
  release exists yet; the next push after 2026-07-10 publishes `agent-v0.1.0`.
- **Controller: PUBLISHED IMAGE (GHCR).** `release-controller.yml` builds `Deploy/controller/Dockerfile`
  and pushes `ghcr.io/matthewjenner/servercenter-controller:<version>` + `:latest` on push (version-gated
  on `VersionPrefix`, tag `controller-v<version>`). On the hypervisor, copy JUST
  `Deploy/controller/docker-compose.yml` + a `templates/` dir (NO source, NO SDK) and run
  `docker compose pull && docker compose up -d`. First time, set the GHCR package visibility to public
  in GitHub -> Packages (public repo), or `docker login ghcr.io` on the hypervisor. Program binds
  `ListenAnyIP` so the container is reachable from remote agents.
- **UI: run from source** on your workstation (`Scripts/run.sh ui`) - it has the code. A UI release
  track (Velopack or a self-contained zip, `ui-v<version>`) is not built yet; add it if you need to run
  the UI on a machine without source.

So for end-to-end testing: install the AGENT from its release; pull + run the CONTROLLER image via
compose on the hypervisor; run the UI from source. See `Docs/linux-smoke-runbook.md` for the ordered
pass, including its Known gaps (no bootstrap-token mint endpoint yet -> plaintext for now; no store
endpoints for descriptors/recipes/instances -> sqlite3 seeding for the P5/P7 steps).
