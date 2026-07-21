# Conventions and standards

The rules that govern how code lands in this repo. If you are new here, read this plus
`Docs/dev-environment.md` (build/run/IDE) and `Docs/testing.md` (test tiers and traps).
ASCII punctuation only - see below.

## Repo shape

- Solution format is **`.slnx`** (the XML solution format), not `.sln`. Build with
  `dotnet build ServerCenter.slnx`.
- Top-level folders use **Title Case** to match dotnet conventions: `Src`, `Tests`, `Contracts`,
  `Docs`, `Deploy`, `Scripts`, `templates`.
- The repo is **public**. No secrets, keys, ARNs, account ids, hostnames, or otherwise doxxable
  content in code, docs, or commit messages. Bucket names and AWS credentials are runtime
  configuration - never committed, never baked into an image.

## Code style (build-enforced)

**No `var`. No top-level statements.** Explicit types on every local, full classes, file-scoped
namespaces. Target-typed `new()` is fine (the type is on the left of the `=`).

This is not advisory: `.editorconfig` sets `csharp_style_var_* = false` and
`csharp_style_namespace_declarations = file_scoped`, `Directory.Build.props` sets
`EnforceCodeStyleInBuild`, and the solution runs `TreatWarningsAsErrors`. A stray `var` or a block
namespace **fails the build**. Bulk-fix with
`dotnet format style ServerCenter.slnx --diagnostics IDE0008 --severity warn` (may need 2-3 passes
to converge).

## Build quality bar

- **`TreatWarningsAsErrors` is ON solution-wide.** NuGet audit findings (NU1903) and analyzer
  warnings (for example xUnit1051) fail the build by design. Fix them; do not suppress broadly.
- Avalonia XAML warnings (AVLNxxxx) come from the XAML compiler, not `csc`, so they do **not** fail
  the build. Fix them anyway.
- `dotnet build ServerCenter.slnx` is the **source of truth**. The IDE language server routinely
  reports phantom CS0246/CS1061 against this repo (generated code + `.slnx` support); see
  `Docs/dev-environment.md`.

## Punctuation and prose

Plain **ASCII punctuation everywhere** - in docs, code, comments, log lines, and UI strings. No
em/en dashes, no unicode ellipsis, no curly quotes. Use `-` and `...`.

## Scripts

All repo scripts are **bash (`.sh`)**, run from Git Bash on Windows. Do not add `.ps1` scripts for
repo tooling; if an interactive Windows launcher is needed, use bash with background jobs and a
trap-based cleanup. Current scripts: `bump-version.sh`, `run.sh`, `publish-agent.sh`, `dev-stack.sh`.

Exception, from Phase 8 onward: Windows **agent/deploy** `.ps1` scripts that ship to a node must run
on **Windows PowerShell 5.x** - ASCII only, no `??` operator, no PowerShell 7 syntax.

## Dependencies

Newest stable by default; roll back only on a real, verified issue. Re-verify pins on any bulk
dependency bump rather than carrying them over from another project.

Pins that are deliberate and must not be removed casually:

| Package | Pin | Why |
| ------- | --- | --- |
| `SQLitePCLRaw.lib.e_sqlite3` | transitive-pinned to 3.53.3 in `Directory.Packages.props` | `Microsoft.Data.Sqlite` 10.0.9 drags in the vulnerable 2.1.11 (NU1903) and the 2.x line has no patched release. Revisit only when Microsoft.Data.Sqlite adopts SQLitePCLRaw 3.x. |
| Assertions library | `AwesomeAssertions` | FluentAssertions >= 7 requires a paid license. Use AwesomeAssertions (or FluentAssertions 6.12.x), never FA >= 7. |
| Avalonia | 12.x | The "stay on Avalonia 11.x" pin some sibling projects carry does **not** apply here - the DataGrid 12.x blocker is resolved and this repo ships on 12.x. Do not re-apply the 11.x pin. |

Test stack is **xunit.v3** (test projects are `OutputType=Exe`) plus AwesomeAssertions.

## Versioning and releases

One repo-wide `<VersionPrefix>` in `Directory.Build.props`, inherited by every project, shared by
all three deployables (agent, controller, UI). Bump it with:

```bash
bash Scripts/bump-version.sh Patch|Minor|Major
```

**Bump on any change to shippable artifacts** - anything under `Src/**`, `Scripts/publish-*`,
`Deploy/**`, the release workflows, or bundled assets. Default to `Patch`; `Minor` for a clear
feature, `Major` for a breaking change. Docs-only changes do not need a bump.

**Why it matters:** releases are **version-gated and idempotent** - the pipeline skips a push whose
version already released. A code change committed on an already-released version therefore publishes
**nothing**. Bumping is the pipeline-native way to actually ship. Keep any hardcoded version in
`README.md` (the quickstart download URLs) in sync with the bump.

Release tags are namespaced per track (`agent-v<version>`, `controller-v<version>`, `ui-v<version>`)
so the three tracks share one `VersionPrefix` but release independently. See
`Docs/build-and-update.md`.

## GitHub Actions

Before pinning or updating any `uses:` in `.github/workflows/*.yml`, **verify the current stable
major** of that action (via its tag's `action.yml` `runs.using`, or
`gh api repos/<owner>/<repo>/releases/latest`). Action majors get retired - the Node 20 to Node 24
runner change retired `actions/checkout@v4` and `actions/setup-dotnet@v4`. Pin to a Node 24 (or
newer) major. Do not copy version numbers from sibling repos.

## Working discipline

- **Contracts before code.** No feature code lands ahead of an approved contract
  (`Docs/phase-0-contracts.md`).
- **`Docs/workplan.md` is the living tracker.** At each phase/slice boundary: check off items,
  refresh Current State, and append to the Decisions Log. Read it first on a cold start.
- **Ship small.** Build, test, and document each step; `dotnet test ServerCenter.slnx` must be green
  before a step is declared done.
- An intermittent test failure is a **real race**, not "transient" - chase it. See
  `Docs/testing.md`.
